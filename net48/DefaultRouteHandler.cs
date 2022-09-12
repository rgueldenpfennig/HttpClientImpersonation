using System;
using System.Web;
using System.Web.Routing;

namespace ReverseProxy
{
    /// <summary>
    /// Represents the default route handler which will be instantiated only once per application lifetime.
    /// </summary>
    public class DefaultRouteHandler : IRouteHandler
    {
        private readonly IHttpHandler _proxyHandler = new ProxyHandler();

        /// <inheritdoc />
        public IHttpHandler GetHttpHandler(RequestContext requestContext)
        {
            if (requestContext is null) throw new ArgumentNullException(nameof(requestContext));

            return _proxyHandler;
        }
    }
}