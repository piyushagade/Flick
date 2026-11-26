using Flick.Core.Actions;
using Xunit;

namespace Flick.Core.Tests;

public sealed class ActionPayloadParserTests
{
    [Fact]
    public void ParseHttp_SupportsGetAndPostWithJsonBody()
    {
        var getSpec = ActionPayloadParser.ParseHttp("https://example.com/ping");
        Assert.NotNull(getSpec);
        Assert.Equal("GET", getSpec!.Method);
        Assert.Equal("https://example.com/ping", getSpec.Url);
        Assert.Null(getSpec.JsonBody);

        var postSpec = ActionPayloadParser.ParseHttp("POST https://api.example.com/hook | {\"on\":true}");
        Assert.NotNull(postSpec);
        Assert.Equal("POST", postSpec!.Method);
        Assert.Equal("https://api.example.com/hook", postSpec.Url);
        Assert.Equal("{\"on\":true}", postSpec.JsonBody);
    }

    [Fact]
    public void BuildIftttUrl_SupportsEventKeyPairAndFullUrl()
    {
        string? fromPair = ActionPayloadParser.BuildIftttUrl("desk_lights|my_secret_key");
        Assert.Equal("https://maker.ifttt.com/trigger/desk_lights/with/key/my_secret_key", fromPair);

        string? full = ActionPayloadParser.BuildIftttUrl("https://maker.ifttt.com/trigger/x/with/key/y");
        Assert.Equal("https://maker.ifttt.com/trigger/x/with/key/y", full);
    }

    [Fact]
    public void ParseMqtt_ParsesBrokerPortTopicPayloadAndCredentials()
    {
        var spec = ActionPayloadParser.ParseMqtt("broker.hivemq.com:1884|home/desk/light|TOGGLE|alice|secret");
        Assert.NotNull(spec);
        Assert.Equal("broker.hivemq.com", spec!.Host);
        Assert.Equal(1884, spec.Port);
        Assert.Equal("home/desk/light", spec.Topic);
        Assert.Equal("TOGGLE", spec.Payload);
        Assert.Equal("alice", spec.Username);
        Assert.Equal("secret", spec.Password);
    }

    [Fact]
    public void BuildMqttPackets_EncodesValidConnectAndPublishHeaders()
    {
        byte[] connect = ActionPayloadParser.BuildMqttConnectPacket("flick-1", "user", "pass");
        Assert.Equal(0x10, connect[0]); // CONNECT packet type

        byte[] publish = ActionPayloadParser.BuildMqttPublishPacket("home/light", "ON");
        Assert.Equal(0x30, publish[0]); // PUBLISH QoS 0
    }
}
