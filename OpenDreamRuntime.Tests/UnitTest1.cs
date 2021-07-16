using NUnit.Framework;
using OpenDreamShared.Net.Packets;
using System.Threading.Tasks;
using System.Diagnostics;
using OpenDreamRuntime.Procs;
using OpenDreamShared.Dream;
using System.Threading;
using System;

namespace OpenDreamRuntime.Tests
{
    class TestConnection : DreamConnection {
        public TestConnection(DreamRuntime runtime)
            : base(runtime)
        {}

        public override byte[] ReadPacketData()
        {
            throw new System.NotImplementedException();
        }

        public override void SendPacket(IPacket packet)
        {
            throw new System.NotImplementedException();
        }
    }

    class TestServer : DreamServer
    {
        public TestServer() {

        }

#pragma warning disable CS0067 // Event is never used
        public override event DreamConnectionReadyEventHandler DreamConnectionRequest;
#pragma warning restore CS0067 // Event is never used

        public override void Start(DreamRuntime runtime)
        {

        }

        public override void Process()
        {

        }
    }

    public class Tests {
        [OneTimeSetUp]
        public void Compile() {
            // Terrible platform-specific way to build our test dependencies
            var info = new ProcessStartInfo {
                FileName = "DMCompiler.exe",
                Arguments = "DMProject\\environment.dme",
                //RedirectStandardInput = true,
                //RedirectStandardError = true,
                UseShellExecute = true,
            };

            var process = new Process { StartInfo = info };
            process.Start();
            process.WaitForExit();

            Assert.AreEqual(0, process.ExitCode);
        }

        private DreamRuntime CreateRuntime() {
            return new DreamRuntime(new TestServer(), "DMProject\\environment.json");
        }

        [Test]
        public void SyncReturn() {
            var runtime = CreateRuntime();

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
            var result = DreamThread.Run(runtime, async (state) => {
                return new DreamValue(1337);
            });
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously

            Assert.AreEqual(new DreamValue(1337), result);
            Assert.Zero(runtime.ExceptionCount);
        }

        [Test]
        public void SyncReturnInAsync() {
            var runtime = CreateRuntime();

            var sync_result = DreamThread.Run(runtime, async(state) => {
                state.Result = new DreamValue(420);
                await Task.Yield();
                return new DreamValue(1337);
            });

            Assert.AreEqual(new DreamValue(420), sync_result);
            Assert.Zero(runtime.ExceptionCount);
        }

        [Test]
        public void SyncCall() {
            var runtime = CreateRuntime();

            var sync_result = DreamThread.Run(runtime, async(state) => {
                var root = state.Runtime.ObjectTree.RootObject.ObjectDefinition;
                var proc = root.GetProc("sync_test");
                return await state.Call(proc, null, null, new DreamProcArguments(null));
            });

            Assert.AreEqual(new DreamValue(1992), sync_result);
            Assert.Zero(runtime.ExceptionCount);
        }

        [Test]
        public void Error() {
            var runtime = CreateRuntime();

            var sync_result = DreamThread.Run(runtime, async(state) => {
                var world = state.Runtime.WorldInstance;
                var proc = world.GetProc("error_test");
                return await state.Call(proc, world, null, new DreamProcArguments(null));
            });

            Assert.AreEqual(new DreamValue(1), sync_result);
            Assert.AreEqual(1, runtime.ExceptionCount);
        }

        [Test]
        public void SyncImage() {
            var runtime = CreateRuntime();

            var sync_result = DreamThread.Run(runtime, async(state) => {
                var world = runtime.WorldInstance;
                var proc = world.GetProc("image_test");
                return await state.Call(proc, world, null, new DreamProcArguments(null));
            });

            var obj = sync_result.GetValueAsDreamObject();
            Assert.IsNotNull(obj);

            var imageDefinition = runtime.ObjectTree.GetObjectDefinitionFromPath(DreamPath.Image);
            Assert.AreEqual(imageDefinition, obj.ObjectDefinition);
            Assert.Zero(runtime.ExceptionCount);
        }

        [Test, Timeout(10000)]
        public void AsyncCall() {
            var runtime = CreateRuntime();
            var result = DreamValue.Null;

            DreamThread.Run(runtime, async(state) => {
                var world = runtime.WorldInstance;
                var proc = world.GetProc("async_test");
                result = await state.Call(proc, world, null, new DreamProcArguments(null));
                state.Runtime.Shutdown = true;
                return DreamValue.Null;
            });

            runtime.Run();

            Assert.AreEqual(new DreamValue(1337), result);
            Assert.Zero(runtime.ExceptionCount);
        }

        [Test]
        public void CrashPropagation() {
            var runtime = CreateRuntime();

            var sync_result = DreamThread.Run(runtime, async(state) => {
                var world = runtime.WorldInstance;
                var proc = world.GetProc("crash_test");
                return await state.Call(proc, world, null, new DreamProcArguments(null));
            });

            Assert.AreEqual(new DreamValue(1), sync_result);
            Assert.AreEqual(1, runtime.ExceptionCount);
        }

        [Test]
        public void StackOverflow() {
            var runtime = CreateRuntime();

            var sync_result = DreamThread.Run(runtime, async(state) => {
                var world = runtime.WorldInstance;
                var proc = world.GetProc("stack_overflow_test");
                return await state.Call(proc, world, null, new DreamProcArguments(null));
            });

            Assert.AreEqual(new DreamValue(1), sync_result);
            Assert.AreEqual(1, runtime.ExceptionCount);
        }

        [Test, Timeout(10000)]
        public void WaitFor() {
            var runtime = CreateRuntime();
            DreamValue result_1 = DreamValue.Null;
            DreamValue result_2 = DreamValue.Null;

            DreamThread.Run(runtime, async(state) => {
                var world = runtime.WorldInstance;
                var proc_1 = world.GetProc("waitfor_1_a");
                result_1 = await state.Call(proc_1, world, null, new DreamProcArguments(null));

                var proc_2 = world.GetProc("waitfor_2_a");
                result_2 = await state.Call(proc_2, world, null, new DreamProcArguments(null));

                state.Runtime.Shutdown = true;
                return DreamValue.Null;
            });

            runtime.Run();

            Assert.AreEqual(new DreamValue(3), result_1);
            Assert.AreEqual(new DreamValue(2), result_2);
            Assert.Zero(runtime.ExceptionCount);
        }


        [Test]
        public void Default() {
            var runtime = CreateRuntime();

            var result = DreamThread.Run(runtime, async(state) => {
                var world = runtime.WorldInstance;
                var proc = world.GetProc("default_test");
                return await state.Call(proc, world, null, new DreamProcArguments(null));
            });

            var obj = result.GetValueAsDreamObjectOfType(DreamPath.Datum);
            Assert.IsNotNull(obj);
            Assert.Zero(runtime.ExceptionCount);
        }

        [Test]
        public void ValueInList() {
            var runtime = CreateRuntime();

            var result = DreamThread.Run(runtime, async(state) => {
                var world = runtime.WorldInstance;
                var proc = world.GetProc("value_in_list");
                return await state.Call(proc, world, null, new DreamProcArguments(null));
            });

            Assert.AreEqual(new DreamValue(1), result);
            Assert.Zero(runtime.ExceptionCount);
        }


        [Test]
        public void CallTest() {
            var runtime = CreateRuntime();

            var result = DreamThread.Run(runtime, async(state) => {
                var world = runtime.WorldInstance;
                var proc = world.GetProc("call_test");
                return await state.Call(proc, world, null, new DreamProcArguments(null));
            });

            Assert.AreEqual(new DreamValue(13), result);
            Assert.Zero(runtime.ExceptionCount);
        }

        [Test]
        public void SuperCallTest() {
            var runtime = CreateRuntime();

            var result = DreamThread.Run(runtime, async(state) => {
                var world = runtime.WorldInstance;
                var proc = world.GetProc("super_call");
                return await state.Call(proc, world, null, new DreamProcArguments(null));
            });

            Assert.AreEqual(new DreamValue(127), result);
            Assert.Zero(runtime.ExceptionCount);
        }

        [Test]
        public void ConditionalAccessTest() {
            var runtime = CreateRuntime();

            var result = DreamThread.Run(runtime, async(state) => {
                var world = runtime.WorldInstance;
                var proc = world.GetProc("conditional_access_test");
                return await state.Call(proc, world, null, new DreamProcArguments(null));
            });

            Assert.AreEqual(new DreamValue(1), result);
            Assert.Zero(runtime.ExceptionCount);
        }

        [Test]
        public void ConditionalAccessErrorTest() {
            var runtime = CreateRuntime();

            var result = DreamThread.Run(runtime, async(state) => {
                var world = runtime.WorldInstance;
                var proc = world.GetProc("conditional_access_test_error");
                return await state.Call(proc, world, null, new DreamProcArguments(null));
            });

            Assert.AreEqual(1, runtime.ExceptionCount);
        }

        [Test]
        public void ConditionalCallTest() {
            var runtime = CreateRuntime();

            var result = DreamThread.Run(runtime, async(state) => {
                var world = runtime.WorldInstance;
                var proc = world.GetProc("conditional_call_test");
                return await state.Call(proc, world, null, new DreamProcArguments(null));
            });

            Assert.AreEqual(DreamValue.Null, result);
            Assert.Zero(runtime.ExceptionCount);
        }

        [Test]
        public void ConditionalCallErrorTest() {
            var runtime = CreateRuntime();

            var result = DreamThread.Run(runtime, async(state) => {
                var world = runtime.WorldInstance;
                var proc = world.GetProc("conditional_call_test_error");
                return await state.Call(proc, world, null, new DreamProcArguments(null));
            });

            Assert.AreEqual(1, runtime.ExceptionCount);
        }

        [Test]
        public void ConditionalMutateTest() {
            var runtime = CreateRuntime();

            var result = DreamThread.Run(runtime, async(state) => {
                var world = runtime.WorldInstance;
                var proc = world.GetProc("conditional_mutate");
                return await state.Call(proc, world, null, new DreamProcArguments(null));
            });

            Assert.AreEqual(new DreamValue(4), result);
            Assert.AreEqual(0, runtime.ExceptionCount);
        }

        [Test]
        public void ListIndexMutateTest() {
            var runtime = CreateRuntime();

            var result = DreamThread.Run(runtime, async(state) => {
                var world = runtime.WorldInstance;
                var proc = world.GetProc("list_index_mutate");
                return await state.Call(proc, world, null, new DreamProcArguments(null));
            });

            Assert.AreEqual(new DreamValue(30), result);
            Assert.AreEqual(0, runtime.ExceptionCount);
        }


        [Test]
        public void SwitchConstTest() {
            var runtime = CreateRuntime();

            var result = DreamThread.Run(runtime, async(state) => {
                var world = runtime.WorldInstance;
                var proc = world.GetProc("switch_const");
                return await state.Call(proc, world, null, new DreamProcArguments(null));
            });

            Assert.AreEqual(new DreamValue(1), result);
            Assert.AreEqual(0, runtime.ExceptionCount);
        }

        [Test]
        public void ClampValueTest() {
            var runtime = CreateRuntime();

            var result = DreamThread.Run(runtime, async (state) => {
                var world = runtime.WorldInstance;
                var proc = world.GetProc("clamp_value");
                return await state.Call(proc, world, null, new DreamProcArguments(null));
            });

            Assert.Zero(runtime.ExceptionCount);
            Assert.AreEqual(new DreamValue(1), result);
        }

        [Test]
        public void Md5Test() {
            var runtime = CreateRuntime();

            var result = DreamThread.Run(runtime, async (state) => {
                var world = runtime.WorldInstance;
                var proc = world.GetProc("md5_test");
                return await state.Call(proc, world, null, new DreamProcArguments(null));
            });

            Assert.Zero(runtime.ExceptionCount);
            Assert.AreEqual(new DreamValue("c74318b61a3024520c466f828c043c79"), result);
        }
    }
}
