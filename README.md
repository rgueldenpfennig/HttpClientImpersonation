# HttpClientImpersonation
.NET Framework 4.8 and .NET 6 samples to impersonate Windows authenticated users in an ASP.NET application

## Proxy functionality
Both applications represent a kind of "impersonating" reverse proxy.

The reverse proxy:
- accepts incoming HTTP requests
- ensures that the client is authenticated by WIA (Windows Integrated Authentication)
- impersonates the user identity
- and forwards the copied but impersonated request to the destination system

The proxy won't forward the destination response back to the client. Instead the proxy response represents the state of the impersonation workflow.

## Installation
In my use case both versions have been deployed on an IIS server. The build binaries need to be copied to the corresponding IIS site folder.

For example locally you can use a file publishing profile to deploy the application from Visual Studio to a local IIS instance.

Ensure to activate both Anonymous and Windows authentication on the IIS site.

## Differences between .NET Framework 4.8 and .NET 6
A great part of the `System.Net` stack has been redesigned since the beginning of the development of .NET Core. Especially the `HttpClient` and the underlying components have been changed in many iterations.

You can find many PRs such as this one that relate to those redesigns: https://github.com/dotnet/runtime/pull/53851

Beside that the impersonation API was also changed to make the usage more "fluent" in cases of asynchronous code usage (async/await): https://github.com/dotnet/runtime/issues/24009

**Behaviour of .NET Framework 4.8**
- no reuse of established TCP connections when `HttpClient` is executing a request in an impersonated scope
	- this leads to a increasing number of TCP connections in `TIME_WAIT` state depending on the throughput which can finally cause a port exhaustion
- `HttpClient` request while in impersonation scope is not asynchronous but blocks a thread during IO
    - due to to certain restrictions in `aspnet.config` I was not able to get it working fully async while ensuring to keep the flow of the identity context
- high CPU load when impersonation of an user identity happens
    - of course this depends on the thorughput but in general impersonation is quite expensive on OS level
- in this case I was using Kerberos (not NTLM) as Windows Integrated Authentication
    - for each impersonation the application was requesting a fresh Kerberos ticket from the Domain Controller
    - this was causing an increased base load on all DCs that were handling those requests

**Behaviour of .NET 6**
- reuse of established TCP connections when `HttpClient` is executing a request in an impersonated scope
    - the port exhaustion issue due to a high number of TCP connections in TIME_WAIT state has been mitigated
    - TCP connections are now being reused as expected
- using `HttpClient` while in an impersonation scope is now fully asynchronous
- reduced CPU load
    - the comparison benchmark was showing a difference of about 10-20% less CPU activity
- when using Kerberos the underlying framework seems to cache the aquired Kerberos tickets for the impersonation process
    - the KDC TGS request rate on all involved DCs was greatly reduced in comparison to .NET Framework 4.8
- throughput of processed requests/second is greatly increased by a factor of 2x
