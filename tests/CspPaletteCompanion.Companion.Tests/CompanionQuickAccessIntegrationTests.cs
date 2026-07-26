using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace CspPaletteCompanion.Companion.Tests;

public sealed class CompanionQuickAccessIntegrationTests
{
    [Fact]
    public async Task GetDataAndDoCommand_EnumeratesCommandItemsInWireOrderAndUsesExactIdentity()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        var hostTask = RunFakeHostAsync(listener, timeout.Token);

        using var tcp = new TcpClient(AddressFamily.InterNetwork);
        await tcp.ConnectAsync(IPAddress.Loopback, endpoint.Port, timeout.Token);
        await using var client = CompanionModeClient.CreateForConnectedStream(
            tcp.GetStream(),
            TimeSpan.FromHours(1));
        await client.AuthenticateAsync(
            "G#1:2026",
            "pairing-password",
            "rotated-password",
            timeout.Token);

        var data = await client.GetQuickAccessDataAsync(timeout.Token);

        Assert.Equal(1, data.CurrentSetIndex);
        Assert.Equal(0, data.RemoteControllerSetIndex);
        Assert.Collection(
            data.Commands,
            command =>
            {
                Assert.Equal((0, 0, 0, 0), (
                    command.SetIndex,
                    command.RowIndex,
                    command.ColumnIndex,
                    command.ItemIndex));
                Assert.Equal("Painting", command.SetName);
                Assert.Equal("set-0", command.SetUuid);
                Assert.Equal("Add color", command.DisplayName);
                Assert.Equal("PaletteCommand", command.CommandType);
                Assert.Equal("AddColor", command.CommandName);
                Assert.True(command.IsEnabled);
                Assert.False(command.IsChecked);
            },
            command =>
            {
                Assert.Equal((0, 0, 1, 0), (
                    command.SetIndex,
                    command.RowIndex,
                    command.ColumnIndex,
                    command.ItemIndex));
                Assert.Equal("Import color set", command.DisplayName);
                Assert.False(command.IsEnabled);
            },
            command =>
            {
                Assert.Equal((1, 0, 0, 0), (
                    command.SetIndex,
                    command.RowIndex,
                    command.ColumnIndex,
                    command.ItemIndex));
                Assert.Equal("Export color set", command.DisplayName);
                Assert.True(command.IsEnabled);
                Assert.True(command.IsChecked);
            });
        Assert.Collection(
            data.EnabledCommands,
            command => Assert.Equal("AddColor", command.CommandName),
            command => Assert.Equal("ExportColorSet", command.CommandName));
        Assert.Collection(
            data.EnabledCommandChoices,
            choice =>
            {
                Assert.Equal(
                    new CompanionQuickAccessCommandIdentity("PaletteCommand", "AddColor"),
                    choice.Identity);
                Assert.Equal("Add color", choice.DisplayName);
                Assert.Equal("Painting", choice.SetName);
                Assert.Equal("set-0", choice.SetUuid);
                Assert.Equal((0, 0, 0, 0), (
                    choice.SetIndex,
                    choice.RowIndex,
                    choice.ColumnIndex,
                    choice.ItemIndex));
            },
            choice =>
            {
                Assert.Equal("ExportColorSet", choice.Identity.CommandName);
                Assert.Equal("File", choice.SetName);
                Assert.Equal("set-1", choice.SetUuid);
            });

        var selected = data.FindEnabledCommand(
            new CompanionQuickAccessCommandIdentity("PaletteCommand", "AddColor"));
        Assert.NotNull(selected);
        Assert.Equal("Add color", selected.DisplayName);

        Assert.Null(data.FindEnabledCommand(
            new CompanionQuickAccessCommandIdentity("palettecommand", "AddColor")));
        Assert.Null(data.FindEnabledCommand(
            new CompanionQuickAccessCommandIdentity("PaletteCommand", "addcolor")));
        Assert.Null(data.FindEnabledCommand(
            new CompanionQuickAccessCommandIdentity("PaletteCommand", "ImportColorSet")));
        Assert.Null(data.FindEnabledCommand(
            new CompanionQuickAccessCommandIdentity("PaletteCommand", "CopyMergedSelection")));

        await client.DoQuickAccessCommandAsync(selected.Identity, timeout.Token);

        await hostTask.WaitAsync(timeout.Token);
        listener.Stop();
    }

    [Fact]
    public async Task DoCommand_RejectsBlankIdentityBeforeWriting()
    {
        await using var client = CompanionModeClient.CreateForConnectedStream(
            new MemoryStream(),
            TimeSpan.FromHours(1));

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.DoQuickAccessCommandAsync("", "AddColor"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.DoQuickAccessCommandAsync("PaletteCommand", " "));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.DoQuickAccessCommandAsync(null!));
    }

    private static async Task RunFakeHostAsync(
        TcpListener listener,
        CancellationToken cancellationToken)
    {
        using var tcp = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = tcp.GetStream();

        var authentication = await CompanionFrameCodec.ReadAsync(
            stream,
            cancellationToken: cancellationToken);
        Assert.Equal("Authenticate", authentication.Command);
        await WriteAsync(
            stream,
            CompanionFrameType.Success,
            authentication,
            """{"AuthErrorReason":"Unknown","RemoteCommandSpecVersionOfServer":"1.0","IsQuickAccessAvailable":true}"""u8.ToArray(),
            cancellationToken);

        var getData = await CompanionFrameCodec.ReadAsync(
            stream,
            cancellationToken: cancellationToken);
        Assert.Equal(CompanionFrameType.Command, getData.Type);
        Assert.Equal("GetQuickAccessData", getData.Command);
        Assert.Null(getData.Detail);
        await WriteAsync(
            stream,
            CompanionFrameType.Success,
            getData,
            """
            {
              "ToolBarData": [
                {
                  "ItemSetName": "Painting",
                  "ItemSetUuid": "set-0",
                  "ItemSet": [
                    [
                      [
                        {
                          "ItemIDType": "Command",
                          "ItemIDCommandType": "PaletteCommand",
                          "ItemIDCommandName": "AddColor",
                          "ItemShowName": "Add color",
                          "ItemIsEnabled": true,
                          "ItemIsChecked": false
                        },
                        {
                          "ItemIDType": "Tool",
                          "ItemIDToolUuid": "tool-pencil",
                          "ItemShowName": "Pencil",
                          "ItemIsEnabled": true
                        }
                      ],
                      [
                        {
                          "ItemIDType": "Command",
                          "ItemIDCommandType": "PaletteCommand",
                          "ItemIDCommandName": "ImportColorSet",
                          "ItemShowName": "Import color set",
                          "ItemIsEnabled": false,
                          "ItemIsChecked": false
                        }
                      ]
                    ]
                  ]
                },
                {
                  "ItemSetName": "File",
                  "ItemSetUuid": "set-1",
                  "ItemSet": [
                    [
                      [
                        {
                          "ItemIDType": "Command",
                          "ItemIDCommandType": "PaletteCommand",
                          "ItemIDCommandName": "ExportColorSet",
                          "ItemShowName": "Export color set",
                          "ItemIsEnabled": true,
                          "ItemIsChecked": true
                        }
                      ]
                    ]
                  ]
                }
              ],
              "ToolBarViewInfo": {
                "ViewInfoCurrentSetIndex": 1,
                "ViewInfoRemoteControllerSetIndex": 0
              }
            }
            """u8.ToArray(),
            cancellationToken);

        var invoke = await CompanionFrameCodec.ReadAsync(
            stream,
            cancellationToken: cancellationToken);
        Assert.Equal(CompanionFrameType.Command, invoke.Type);
        Assert.Equal("DoQuickAccess", invoke.Command);
        var detail = Assert.IsType<JsonElement>(invoke.Detail);
        Assert.Equal("Command", detail.GetProperty("ItemType").GetString());
        Assert.Equal("PaletteCommand", detail.GetProperty("ItemCommandType").GetString());
        Assert.Equal("AddColor", detail.GetProperty("ItemCommandName").GetString());
        await WriteAsync(
            stream,
            CompanionFrameType.Success,
            invoke,
            Array.Empty<byte>(),
            cancellationToken);
    }

    private static async Task WriteAsync(
        Stream stream,
        CompanionFrameType type,
        CompanionFrame request,
        ReadOnlyMemory<byte> detail,
        CancellationToken cancellationToken)
    {
        var response = CompanionFrameCodec.EncodeRaw(
            type,
            request.Command,
            request.Serial,
            detail.Span);
        await stream.WriteAsync(response, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
