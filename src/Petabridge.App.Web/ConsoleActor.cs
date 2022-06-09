using Akka.Actor;
using Akka.Event;

namespace Petabridge.App.Web;

public sealed class ConsoleActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    public ConsoleActor()
    {
        ReceiveAny(_ =>
        {
            _log.Info("Received: {0}", _);
            Sender.Tell(_);
        });
    }
}