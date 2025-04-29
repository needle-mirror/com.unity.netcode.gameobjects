using System;
using NUnit.Framework;
using UnityEngine;

namespace Unity.Netcode.EditorTests
{
    /// <summary>
    /// Tests for running a <see cref="NetworkTimeSystem"/> as a client.
    /// </summary>
    public class ClientNetworkTimeSystemTests
    {
        private const double k_AcceptableRttOffset = 0.03d; // 30ms offset is fine

        /// <summary>
        /// Tests whether time is stable if RTT is stable.
        /// </summary>
        [Test]
        public void StableRttTest()
        {
            double receivedServerTime = 2;
            var baseRtt = 0.1f;
            var halfRtt = 0.05f;
            var timeSystem = new NetworkTimeSystem(0.05d, 0.05d, baseRtt);
            timeSystem.Reset(receivedServerTime, 0.15);
            var tickSystem = new NetworkTickSystem(60, timeSystem.LocalTime, timeSystem.ServerTime);

            Assert.True(timeSystem.LocalTime > 2);

            var steps = TimingTestHelper.GetRandomTimeSteps(100f, 0.01f, baseRtt, 42);
            var rttSteps = TimingTestHelper.GetRandomTimeSteps(1000f, baseRtt - 0.05f, baseRtt + 0.05f, 42); // 10ms jitter

            // run for a while so that we reach regular RTT offset
            TimingTestHelper.ApplySteps(timeSystem, tickSystem, steps, delegate (int step)
            {
                // sync network stats
                receivedServerTime += steps[step];
                timeSystem.Sync(receivedServerTime, rttSteps[step]);
            });

            // check how we close we are to target time.
            var offsetToTarget = (timeSystem.LocalTime - timeSystem.ServerTime) - halfRtt - timeSystem.ServerBufferSec - timeSystem.LocalBufferSec;
            Debug.Log($"offset to target time after running for a while: {offsetToTarget}");

            // server speedup/slowdowns should not be affected by RTT
            Assert.True(Math.Abs(offsetToTarget) < k_AcceptableRttOffset, $"Expected offset time to be less than {k_AcceptableRttOffset}ms but it was {offsetToTarget}!");

            // run again, test that we never need to speed up or slow down under stable RTT
            TimingTestHelper.ApplySteps(timeSystem, tickSystem, steps, delegate (int step)
            {
                // sync network stats
                receivedServerTime += steps[step];
                timeSystem.Sync(receivedServerTime, rttSteps[step]);
            });

            // check again to ensure we are still close to the target
            var newOffsetToTarget = (timeSystem.LocalTime - timeSystem.ServerTime) - halfRtt - timeSystem.ServerBufferSec - timeSystem.LocalBufferSec;
            Debug.Log($"offset to target time after running longer: {newOffsetToTarget}");
            // server speedup/slowdowns should not be affected by RTT
            Assert.True(Math.Abs(offsetToTarget) < k_AcceptableRttOffset, $"Expected offset time to be less than {k_AcceptableRttOffset}ms but it was {offsetToTarget}!");

            // difference between first and second offset should be minimal
            var dif = offsetToTarget - newOffsetToTarget;
            Assert.IsTrue(Math.Abs(dif) < 0.01d); // less than 10ms
        }

        /// <summary>
        /// Tests whether local time can speed up and slow down to catch up when RTT changes.
        /// </summary>
        [Test]
        public void RttCatchupSlowdownTest()
        {
            double receivedServerTime = 2;
            var baseRtt = 0.1f;
            var halfRtt = 0.05f;
            var timeSystem = new NetworkTimeSystem(0.05d, 0.05d, baseRtt);
            timeSystem.Reset(receivedServerTime, 0.15);
            var tickSystem = new NetworkTickSystem(60, timeSystem.LocalTime, timeSystem.ServerTime);

            var steps = TimingTestHelper.GetRandomTimeSteps(100f, 0.01f, baseRtt, 42);
            var rttSteps = TimingTestHelper.GetRandomTimeSteps(1000f, baseRtt - 0.05f, baseRtt + 0.05f, 42); // 10ms jitter

            // run for a while so that we reach regular RTT offset
            TimingTestHelper.ApplySteps(timeSystem, tickSystem, steps, delegate (int step)
            {
                // sync network stats
                receivedServerTime += steps[step];
                timeSystem.Sync(receivedServerTime, rttSteps[step]);
            });

            // increase RTT to ~200ms from ~100ms
            var rttSteps2 = TimingTestHelper.GetRandomTimeSteps(1000f, 0.195f, 0.205f, 42);

            double unscaledLocalTime = timeSystem.LocalTime;
            double unscaledServerTime = timeSystem.ServerTime;
            TimingTestHelper.ApplySteps(timeSystem, tickSystem, steps, delegate (int step)
             {
                 // sync network stats
                 unscaledLocalTime += steps[step];
                 unscaledServerTime += steps[step];
                 receivedServerTime += steps[step];
                 timeSystem.Sync(receivedServerTime, rttSteps2[step]);
             });

            var totalLocalSpeedUpTime = timeSystem.LocalTime - unscaledLocalTime;
            var totalServerSpeedUpTime = timeSystem.ServerTime - unscaledServerTime;

            // speed up of 0.1f expected
            Debug.Log($"Total local speed up time catch up: {totalLocalSpeedUpTime}");
            var expectedSpeedUpTime = Math.Abs(totalLocalSpeedUpTime - halfRtt);
            var expectedServerSpeedUpTime = Math.Abs(totalServerSpeedUpTime);
            Assert.True(expectedSpeedUpTime < k_AcceptableRttOffset, $"Expected local speed up time to be less than {k_AcceptableRttOffset}ms but it was {expectedSpeedUpTime}!");
            // server speedup/slowdowns should not be affected by RTT
            Assert.True(Math.Abs(totalServerSpeedUpTime) < k_AcceptableRttOffset, $"Expected server speed up time to be less than {k_AcceptableRttOffset}ms but it was {expectedServerSpeedUpTime}!");


            // run again with RTT ~100ms and see whether we slow down by -halfRtt
            unscaledLocalTime = timeSystem.LocalTime;
            unscaledServerTime = timeSystem.ServerTime;

            TimingTestHelper.ApplySteps(timeSystem, tickSystem, steps, delegate (int step)
            {
                // sync network stats
                unscaledLocalTime += steps[step];
                unscaledServerTime += steps[step];
                receivedServerTime += steps[step];
                timeSystem.Sync(receivedServerTime, rttSteps[step]);
            });

            totalLocalSpeedUpTime = timeSystem.LocalTime - unscaledLocalTime;
            totalServerSpeedUpTime = timeSystem.ServerTime - unscaledServerTime;
            // slow down of half halfRtt expected
            Debug.Log($"Total local speed up time slow down: {totalLocalSpeedUpTime}");
            expectedSpeedUpTime = Math.Abs(totalLocalSpeedUpTime + halfRtt);
            expectedServerSpeedUpTime = Math.Abs(totalServerSpeedUpTime);
            Assert.True(expectedSpeedUpTime < k_AcceptableRttOffset, $"Expected local speed up time to be less than {k_AcceptableRttOffset}ms but it was {expectedSpeedUpTime}!");
            // server speedup/slowdowns should not be affected by RTT
            Assert.True(Math.Abs(totalServerSpeedUpTime) < k_AcceptableRttOffset, $"Expected server speed up time to be less than {k_AcceptableRttOffset}ms but it was {expectedServerSpeedUpTime}!");
        }

        /// <summary>
        /// Tests whether time resets when there is a huge spike in RTT and is able to stabilize again.
        /// </summary>
        [Test]
        public void ResetTest()
        {
            double receivedServerTime = 2;

            var timeSystem = new NetworkTimeSystem(0.05d, 0.05d, 0.1d);
            timeSystem.Reset(receivedServerTime, 0.15);
            var tickSystem = new NetworkTickSystem(60, timeSystem.LocalTime, timeSystem.ServerTime);

            var steps = TimingTestHelper.GetRandomTimeSteps(100f, 0.01f, 0.1f, 42);
            var rttSteps = TimingTestHelper.GetRandomTimeSteps(1000f, 0.095f, 0.105f, 42); // 10ms jitter

            // run for a while so that we reach regular RTT offset
            TimingTestHelper.ApplySteps(timeSystem, tickSystem, steps, delegate (int step)
            {
                // sync network stats
                receivedServerTime += steps[step];
                timeSystem.Sync(receivedServerTime, rttSteps[step]);
            });


            // increase RTT to ~500ms from ~100ms
            var rttSteps2 = TimingTestHelper.GetRandomTimeSteps(1000f, 0.495f, 0.505f, 42);

            // run a single advance expect a hard rest

            receivedServerTime += 1 / 60d;
            timeSystem.Sync(receivedServerTime, 0.5);
            bool reset = timeSystem.Advance(1 / 60d);
            Assert.IsTrue(reset);

            TimingTestHelper.ApplySteps(timeSystem, tickSystem, steps, delegate (int step, bool reset)
            {
                Assert.IsFalse(reset);

                // sync network stats
                receivedServerTime += steps[step];
                timeSystem.Sync(receivedServerTime, rttSteps2[step]);

                // after hard reset time should stay close to half rtt
                var expectedRtt = 0.25d;
                Assert.IsTrue(Math.Abs((timeSystem.LocalTime - timeSystem.ServerTime) - expectedRtt - timeSystem.ServerBufferSec - timeSystem.LocalBufferSec) < k_AcceptableRttOffset);

            });
        }
    }
}
