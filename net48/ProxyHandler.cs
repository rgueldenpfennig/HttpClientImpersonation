using System;
using System.Configuration;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace ReverseProxy
{
    public class ProxyHandler : HttpTaskAsyncHandler
    {
        private readonly Uri _destination;

        /// <summary>
        /// Http client used for impersonated requests.
        /// </summary>
        public HttpClient ImpersonatedClient { get; }

        /// <summary>
        /// Http client used for anonymous requests.
        /// </summary>
        public HttpClient AnonymousClient { get; }

        public override bool IsReusable => true;

        public ProxyHandler()
        {
            var targetUrl = ConfigurationManager.AppSettings["ProxyDestinationUrl"];

            _destination = new Uri(targetUrl, UriKind.Absolute);

            ImpersonatedClient = new HttpClient(new HttpClientHandler
            {
                UseDefaultCredentials = true,
                PreAuthenticate = true,
                AllowAutoRedirect = false
            });

            AnonymousClient = new HttpClient(new HttpClientHandler()
            {
                AllowAutoRedirect = false
            });
        }

        public override Task ProcessRequestAsync(HttpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            context.Response.AddHeader("X-Proxy-Framework", "net48");

            if (context.User == null)
            {
                context.Response.Write("You are not authorized");
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                return Task.CompletedTask;
            }

            var identity = (WindowsIdentity)context.User.Identity;
            if (identity.IsAnonymous)
            {
                context.Response.Write("Your windows identity could not be resolved");
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                return Task.CompletedTask;
            }

            return ForwardRequestAsync(context, identity);
        }

        /// <summary>
        /// Proxies the request to the destination server.
        /// </summary>
        /// <param name="context">The current HttpContext.</param>
        /// <param name="identity">Identity of the current user (which will be impersonated) or null if anonymous.</param>
        private async Task ForwardRequestAsync(
            HttpContext context,
            WindowsIdentity identity = null)
        {
            var destinationRequest = new HttpRequestMessage(HttpMethod.Get, _destination);

            HttpResponseMessage response;
            try
            {
                if (identity != null)
                {
                    context.Response.AddHeader("X-Proxy-Impersonated", "true");
                    using (identity.Impersonate())
                    {
                        // suppress flow of execution context when executing the inner HttpClientHandler
                        // see also: https://stackoverflow.com/a/39971816
                        // due to impersonation we are not able to async/await
                        using (ExecutionContext.SuppressFlow())
                        {
                            response = ImpersonatedClient.SendAsync(destinationRequest, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                        }
                    }
                }
                else
                {
                    context.Response.AddHeader("X-Proxy-Impersonated", "false");
                    response = await AnonymousClient.SendAsync(destinationRequest, HttpCompletionOption.ResponseHeadersRead);
                }
            }
            catch (Exception e)
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                context.Response.Write(e.Message);
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
                    context.Response.Write(identity.Name);
                }
                else
                {
                    context.Response.Write("Anonymous");
                }
            }
        }
    }
}