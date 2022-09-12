using System.Web.Routing;

namespace ReverseProxy
{
    public static class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            var routeHandler = new DefaultRouteHandler();
            routes.Add(new Route("{*url}", routeHandler));
        }
    }
}