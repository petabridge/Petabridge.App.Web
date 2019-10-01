using Akka.Configuration;
using Akka.Actor.Dsl;
using Akka.TestKit.Xunit2;
using Xunit;
using Xunit.Abstractions;
using Akka.Actor;

namespace Petabridge.App.Tests
{
    public class UnitTest1 : TestKit
    {
        public static Config GetTestConfig()
        {
            return @"
                akka{

                }
            ";
        }

        public UnitTest1(ITestOutputHelper helper) : base(GetTestConfig(), output: helper)
        {

        }

        [Fact]
        public void TestMethod1()
        {
            var actor = Sys.ActorOf(act =>
            {
                act.ReceiveAny(((o, context) =>
                {
                    context.Sender.Tell(o);
                }));
            });

            actor.Tell("hit");
            ExpectMsg("hit");
        }
    }
}
