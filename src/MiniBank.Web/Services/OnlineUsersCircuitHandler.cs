using Microsoft.AspNetCore.Components.Server.Circuits;
using System.Security.Claims;

namespace MiniBank.Web.Services
{
    public sealed class OnlineUsersCircuitHandler : CircuitHandler
    {
        private readonly OnlineUsersTracker _tracker;
        private readonly IHttpContextAccessor _http;

        public OnlineUsersCircuitHandler(OnlineUsersTracker tracker, IHttpContextAccessor http)
        {
            _tracker = tracker;
            _http = http;
        }

        public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            try
            {
                var user = _http.HttpContext?.User;
                if (user?.Identity?.IsAuthenticated == true)
                    _tracker.Touch(user, _http.HttpContext?.Request?.Path.Value);
            }
            catch { }

            return Task.CompletedTask;
        }

        public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            try
            {
                var user = _http.HttpContext?.User;
                if (user?.Identity?.IsAuthenticated == true)
                    _tracker.Touch(user, _http.HttpContext?.Request?.Path.Value);
            }
            catch { }

            return Task.CompletedTask;
        }

        public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            // Do not remove immediately؛ معیار اصلی ما LastSeen است.
            // این متد فقط برای اینکه یک Touch آخر ثبت شود.
            try
            {
                var user = _http.HttpContext?.User;
                if (user?.Identity?.IsAuthenticated == true)
                    _tracker.Touch(user, _http.HttpContext?.Request?.Path.Value);
            }
            catch { }

            return Task.CompletedTask;
        }
    }
}
