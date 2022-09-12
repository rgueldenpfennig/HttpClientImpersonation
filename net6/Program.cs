using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.Versioning;
using System.Security.Principal;

internal class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.Configure<IISServerOptions>(options =>
        {
            options.AutomaticAuthentication = true;
        });

        var app = builder.Build();

        var proxyHandler = new ProxyHandler(app.Configuration);

        app.Run(proxyHandler.ProcessRequestAsync);

        app.Run();
    }
}

public class ProxyHandler
{
    private readonly static HttpClient _anonymousClient = new HttpClient(new SocketsHttpHandler
    {
        AllowAutoRedirect = false
    });

    private readonly static HttpClient _impersonatedClient = new HttpClient(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        Credentials = CredentialCache.DefaultCredentials,
        PreAuthenticate = true
    });

    private readonly Uri _destination;

    public ProxyHandler(IConfiguration configuration)
    {
        _destination = new Uri(configuration.GetValue<string>("ProxyDestinationUrl"));
    }

    [SupportedOSPlatformGuard("windows")]
    public async Task ProcessRequestAsync(HttpContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        context.Response.Clear();
        context.Response.Headers.Add("X-Proxy-Framework", "net6");

        if (!OperatingSystem.IsWindows())
        {
            throw new NotSupportedException("Only Windows is supported");
        }

        if (context.User == null)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await context.Response.WriteAsync("You are not authorized");
            return;
        }

        var identity = context.User.Identity as WindowsIdentity;
        if (identity == null || identity.IsAnonymous)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await context.Response.WriteAsync("Your windows identity could not be resolved");
            return;
        }

        await ForwardRequestAsync(context, identity);
    }

    [SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
    private async Task ForwardRequestAsync(
            HttpContext context,
            WindowsIdentity? identity = null)
    {
        var destinationRequest = new HttpRequestMessage(HttpMethod.Get, _destination);

        HttpResponseMessage? response = null;
        try
        {
            if (identity != null)
            {
                context.Response.Headers.Add("X-Proxy-Impersonated", "true");
                response = await WindowsIdentity.RunImpersonatedAsync(
                    identity.AccessToken,
                    () => _impersonatedClient.SendAsync(destinationRequest, HttpCompletionOption.ResponseHeadersRead));
            }
            else
            {
                context.Response.Headers.Add("X-Proxy-Impersonated", "false");
                response = await _anonymousClient.SendAsync(destinationRequest, HttpCompletionOption.ResponseHeadersRead);
            }
        }
        catch (Exception e)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
            await context.Response.WriteAsync(e.Message);
            return;
        }
        finally
        {
            destinationRequest.Dispose();
        }

        using (response)
        {
            context.Response.StatusCode = (int)HttpStatusCode.OK;

            if (identity != null)
            {
                await context.Response.WriteAsync(identity.Name);
            }
            else
            {
                await context.Response.WriteAsync("Anonymous");
            }
        }
    }
}