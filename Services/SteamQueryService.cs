using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RustServerHealth.Services;

public class ServerInfo
{
    public string Name { get; set; } = "";
    public string Map { get; set; } = "";
    public int Players { get; set; }
    public int MaxPlayers { get; set; }
    public int Bots { get; set; }
    public bool HasPassword { get; set; }
    public bool IsVac { get; set; }
    public string Version { get; set; } = "";
    public string? Keywords { get; set; }
}

public class PlayerInfo
{
    public string Name { get; set; } = "";
    public int Score { get; set; }
    public TimeSpan Duration { get; set; }
}

public class SteamQueryService
{
    private readonly string _host;
    private readonly int _port;
    private readonly ILogger<SteamQueryService> _logger;
    private const int TimeoutMs = 3000;

    // A2S_INFO challenge-capable request payload
    private static readonly byte[] A2S_INFO_REQUEST =
    [
        0xFF, 0xFF, 0xFF, 0xFF, 0x54,
        0x53, 0x6F, 0x75, 0x72, 0x63, 0x65, 0x20, 0x45, 0x6E, 0x67, 0x69,
        0x6E, 0x65, 0x20, 0x51, 0x75, 0x65, 0x72, 0x79, 0x00
    ];

    public SteamQueryService(string host, int port, ILogger<SteamQueryService> logger)
    {
        _host = host;
        _port = port;
        _logger = logger;
    }

    public async Task<ServerInfo?> GetServerInfoAsync()
    {
        try
        {
            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = TimeoutMs;
            udp.Client.SendTimeout = TimeoutMs;

            var endpoint = new IPEndPoint(await ResolveAsync(_host), _port);

            await udp.SendAsync(A2S_INFO_REQUEST, endpoint);
            var result = await ReceiveWithTimeoutAsync(udp);
            if (result is null) return null;

            // Challenge response — server requires a payload with the challenge bytes appended
            if (result.Length >= 9 && result[4] == 0x41)
            {
                var challenged = new byte[A2S_INFO_REQUEST.Length + 4];
                A2S_INFO_REQUEST.CopyTo(challenged, 0);
                challenged[^4] = result[5];
                challenged[^3] = result[6];
                challenged[^2] = result[7];
                challenged[^1] = result[8];
                await udp.SendAsync(challenged, endpoint);
                result = await ReceiveWithTimeoutAsync(udp);
                if (result is null) return null;
            }

            return ParseServerInfo(result);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("A2S_INFO failed: {Msg}", ex.Message);
            return null;
        }
    }

    public async Task<List<PlayerInfo>> GetPlayersAsync()
    {
        try
        {
            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = TimeoutMs;
            udp.Client.SendTimeout = TimeoutMs;

            var endpoint = new IPEndPoint(await ResolveAsync(_host), _port);

            // Step 1: request challenge
            byte[] challengeReq = [0xFF, 0xFF, 0xFF, 0xFF, 0x55, 0xFF, 0xFF, 0xFF, 0xFF];
            await udp.SendAsync(challengeReq, endpoint);
            var challengeResp = await ReceiveWithTimeoutAsync(udp);
            if (challengeResp is null || challengeResp.Length < 9 || challengeResp[4] != 0x41)
                return [];

            // Step 2: send A2S_PLAYER with challenge
            byte[] playerReq =
            [
                0xFF, 0xFF, 0xFF, 0xFF, 0x55,
                challengeResp[5], challengeResp[6], challengeResp[7], challengeResp[8]
            ];
            await udp.SendAsync(playerReq, endpoint);
            var playerResp = await ReceiveWithTimeoutAsync(udp);
            return playerResp is null ? [] : ParsePlayers(playerResp);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("A2S_PLAYER failed: {Msg}", ex.Message);
            return [];
        }
    }

    private static ServerInfo? ParseServerInfo(byte[] data)
    {
        // Header: FF FF FF FF 49 ...
        if (data.Length < 6 || data[4] != 0x49) return null;

        using var ms = new MemoryStream(data, 5, data.Length - 5);
        using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: false);
        try
        {
            reader.ReadByte(); // protocol
            var name = ReadString(reader);
            var map = ReadString(reader);
            ReadString(reader); // folder
            ReadString(reader); // game
            reader.ReadInt16(); // appId
            var players = reader.ReadByte();
            var maxPlayers = reader.ReadByte();
            var bots = reader.ReadByte();
            reader.ReadByte(); // server type
            reader.ReadByte(); // OS
            var hasPassword = reader.ReadByte() == 1;
            var isVac = reader.ReadByte() == 1;
            var version = ReadString(reader);

            string? keywords = null;
            if (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                var edf = reader.ReadByte();
                if ((edf & 0x80) != 0) reader.ReadInt16();
                if ((edf & 0x10) != 0) reader.ReadUInt64();
                if ((edf & 0x40) != 0) { reader.ReadInt16(); ReadString(reader); }
                if ((edf & 0x20) != 0) keywords = ReadString(reader);
            }

            return new ServerInfo
            {
                Name = name, Map = map, Players = players, MaxPlayers = maxPlayers,
                Bots = bots, HasPassword = hasPassword, IsVac = isVac,
                Version = version, Keywords = keywords
            };
        }
        catch { return null; }
    }

    private static List<PlayerInfo> ParsePlayers(byte[] data)
    {
        var list = new List<PlayerInfo>();
        // Header: FF FF FF FF 44 <count>
        if (data.Length < 6 || data[4] != 0x44) return list;

        var count = data[5];
        using var ms = new MemoryStream(data, 6, data.Length - 6);
        using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: false);
        try
        {
            for (int i = 0; i < count; i++)
            {
                reader.ReadByte(); // index
                var name = ReadString(reader);
                var score = reader.ReadInt32();
                var seconds = reader.ReadSingle();
                list.Add(new PlayerInfo
                {
                    Name = name,
                    Score = score,
                    Duration = TimeSpan.FromSeconds(seconds)
                });
            }
        }
        catch { /* partial list is fine */ }
        return list;
    }

    private static string ReadString(BinaryReader reader)
    {
        var sb = new StringBuilder();
        byte b;
        while ((b = reader.ReadByte()) != 0)
            sb.Append((char)b);
        return sb.ToString();
    }

    private static async Task<byte[]?> ReceiveWithTimeoutAsync(UdpClient udp)
    {
        try
        {
            var result = await udp.ReceiveAsync()
                .WaitAsync(TimeSpan.FromMilliseconds(TimeoutMs));
            return result.Buffer;
        }
        catch { return null; }
    }

    private static async Task<IPAddress> ResolveAsync(string host)
    {
        if (IPAddress.TryParse(host, out var ip)) return ip;
        var addresses = await Dns.GetHostAddressesAsync(host);
        return addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            ?? addresses.First();
    }
}
