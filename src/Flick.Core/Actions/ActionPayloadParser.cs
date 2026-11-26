using System.Text;

namespace Flick.Core.Actions;

public sealed record HttpActionSpec(string Method, string Url, string? JsonBody);

public sealed record MqttActionSpec(
    string Host,
    int Port,
    string Topic,
    string Payload,
    string? Username,
    string? Password);

/// <summary>
/// Parses payloads and builds protocol packets for HTTP, IFTTT, and MQTT actions.
/// </summary>
public static class ActionPayloadParser
{
    /// <summary>
    /// Parses formats like:
    /// - "https://example.com/hook" (GET)
    /// - "POST https://example.com/hook | {\"on\":true}"
    /// - "PUT https://example.com/item | {\"v\":1}"
    /// </summary>
    public static HttpActionSpec? ParseHttp(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string trimmed = raw.Trim();
        string? body = null;
        int pipeIdx = trimmed.IndexOf('|');
        if (pipeIdx >= 0)
        {
            body = trimmed[(pipeIdx + 1)..].Trim();
            trimmed = trimmed[..pipeIdx].Trim();
        }

        string method = body is not null ? "POST" : "GET";
        string url = trimmed;

        string[] knownMethods = ["GET", "POST", "PUT", "PATCH", "DELETE"];
        foreach (string m in knownMethods)
        {
            if (trimmed.StartsWith(m + " ", StringComparison.OrdinalIgnoreCase))
            {
                method = m;
                url = trimmed[(m.Length + 1)..].Trim();
                break;
            }
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return null;
        }

        return new HttpActionSpec(method, url, body);
    }

    /// <summary>
    /// Builds an IFTTT Maker Webhook URL from either a full URL or "eventName|key".
    /// </summary>
    public static string? BuildIftttUrl(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string trimmed = raw.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var parts = trimmed.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || string.IsNullOrEmpty(parts[0]) || string.IsNullOrEmpty(parts[1]))
        {
            return null;
        }

        return $"https://maker.ifttt.com/trigger/{Uri.EscapeDataString(parts[0])}/with/key/{Uri.EscapeDataString(parts[1])}";
    }

    /// <summary>
    /// Parses formats like:
    /// - "broker.hivemq.com:1883|home/desk/light|TOGGLE"
    /// - "localhost|test/topic|hello|user|pass"
    /// </summary>
    public static MqttActionSpec? ParseMqtt(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var parts = raw.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || string.IsNullOrEmpty(parts[0]) || string.IsNullOrEmpty(parts[1]))
        {
            return null;
        }

        string hostPart = parts[0];
        string host = hostPart;
        int port = 1883;
        int colon = hostPart.LastIndexOf(':');
        if (colon > 0 && int.TryParse(hostPart[(colon + 1)..], out int parsedPort))
        {
            host = hostPart[..colon];
            port = parsedPort;
        }

        string topic = parts[1];
        string payload = parts.Length >= 3 ? parts[2] : string.Empty;
        string? user = parts.Length >= 4 && !string.IsNullOrEmpty(parts[3]) ? parts[3] : null;
        string? pass = parts.Length >= 5 && !string.IsNullOrEmpty(parts[4]) ? parts[4] : null;

        return new MqttActionSpec(host, port, topic, payload, user, pass);
    }

    /// <summary>
    /// Encodes a minimal MQTT v3.1.1 CONNECT packet.
    /// </summary>
    public static byte[] BuildMqttConnectPacket(string clientId, string? username = null, string? password = null)
    {
        using var body = new MemoryStream();
        // Protocol Name "MQTT" + Level 4 (3.1.1)
        WriteMqttString(body, "MQTT");
        body.WriteByte(0x04);

        byte flags = 0x02; // Clean Session
        if (!string.IsNullOrEmpty(username)) flags |= 0x80;
        if (!string.IsNullOrEmpty(password)) flags |= 0x40;
        body.WriteByte(flags);

        // Keep Alive = 15s
        body.WriteByte(0x00);
        body.WriteByte(0x0F);

        WriteMqttString(body, clientId);
        if (!string.IsNullOrEmpty(username)) WriteMqttString(body, username);
        if (!string.IsNullOrEmpty(password)) WriteMqttString(body, password);

        return WrapMqttPacket(0x10, body.ToArray());
    }

    /// <summary>
    /// Encodes an MQTT v3.1.1 QoS-0 PUBLISH packet.
    /// </summary>
    public static byte[] BuildMqttPublishPacket(string topic, string payload)
    {
        using var body = new MemoryStream();
        WriteMqttString(body, topic);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
        body.Write(payloadBytes, 0, payloadBytes.Length);
        return WrapMqttPacket(0x30, body.ToArray());
    }

    private static void WriteMqttString(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        stream.WriteByte((byte)(bytes.Length >> 8));
        stream.WriteByte((byte)(bytes.Length & 0xFF));
        stream.Write(bytes, 0, bytes.Length);
    }

    private static byte[] WrapMqttPacket(byte header, byte[] body)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(header);
        int len = body.Length;
        do
        {
            byte encoded = (byte)(len % 128);
            len /= 128;
            if (len > 0) encoded |= 0x80;
            ms.WriteByte(encoded);
        } while (len > 0);
        ms.Write(body, 0, body.Length);
        return ms.ToArray();
    }
}
