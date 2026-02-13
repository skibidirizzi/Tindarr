using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tindarr.Application.Options;
using Tindarr.Infrastructure.Integrations.Plex;

namespace Tindarr.UnitTests.Infrastructure.Plex;

public sealed class PlexAuthClientTests
{
	[Fact]
	public async Task GetServersAsync_ParsesJsonArray()
	{
		var body = """
		[
		  {
		    "name": "My Server",
		    "clientIdentifier": "client-1",
		    "machineIdentifier": "machine-1",
		    "accessToken": "server-token",
		    "provides": "server",
		    "owned": true,
		    "presence": true,
		    "productVersion": "1.2.3.4",
		    "platform": "Windows",
		    "connections": [
		      { "uri": "http://127.0.0.1:32400", "protocol": "http", "local": true, "relay": false }
		    ]
		  }
		]
		""";

		var client = CreateClient(body, "application/json");
		var servers = await client.GetServersAsync("client-id", "auth-token", CancellationToken.None);

		Assert.Single(servers);
		var server = servers[0];
		Assert.Equal("machine-1", server.MachineIdentifier);
		Assert.Equal("My Server", server.Name);
		Assert.True(server.Owned);
		Assert.True(server.Online);
		Assert.Equal("server-token", server.AccessToken);
		Assert.Single(server.Connections);
		Assert.Equal("http://127.0.0.1:32400", server.Connections[0].Uri);
	}

	[Fact]
	public async Task GetServersAsync_ParsesWrappedJson_MediaContainerDevice()
	{
		var body = """
		{
		  "MediaContainer": {
		    "Device": [
		      {
		        "name": "Wrapped Server",
		        "machineIdentifier": "machine-2",
		        "provides": "server",
		        "owned": true,
		        "presence": true,
		        "connections": [
		          { "uri": "https://example:32400", "protocol": "https", "local": false, "relay": false }
		        ]
		      }
		    ]
		  }
		}
		""";

		var client = CreateClient(body, "application/json");
		var servers = await client.GetServersAsync("client-id", "auth-token", CancellationToken.None);

		Assert.Single(servers);
		Assert.Equal("machine-2", servers[0].MachineIdentifier);
		Assert.Equal("Wrapped Server", servers[0].Name);
		Assert.Single(servers[0].Connections);
		Assert.Equal("https://example:32400", servers[0].Connections[0].Uri);
	}

	[Fact]
	public async Task GetServersAsync_ParsesXmlFallback()
	{
		var body = """
		<MediaContainer size="1">
		  <Device name="Xml Server" machineIdentifier="machine-3" provides="server" owned="1" presence="1" accessToken="xml-token" productVersion="9.9.9" platform="Linux">
		    <Connection uri="http://10.0.0.2:32400" local="1" relay="0" protocol="http" />
		  </Device>
		</MediaContainer>
		""";

		var client = CreateClient(body, "application/xml");
		var servers = await client.GetServersAsync("client-id", "auth-token", CancellationToken.None);

		Assert.Single(servers);
		Assert.Equal("machine-3", servers[0].MachineIdentifier);
		Assert.Equal("Xml Server", servers[0].Name);
		Assert.True(servers[0].Owned);
		Assert.True(servers[0].Online);
		Assert.Equal("xml-token", servers[0].AccessToken);
		Assert.Single(servers[0].Connections);
	}

	private static PlexAuthClient CreateClient(string body, string contentType)
	{
		var handler = new StaticResponseHandler(body, contentType);
		var httpClient = new HttpClient(handler);
		var options = Options.Create(new PlexOptions());
		return new PlexAuthClient(httpClient, options, NullLogger<PlexAuthClient>.Instance);
	}

	private sealed class StaticResponseHandler(string body, string contentType) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var response = new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(body, Encoding.UTF8, contentType)
			};
			return Task.FromResult(response);
		}
	}
}
