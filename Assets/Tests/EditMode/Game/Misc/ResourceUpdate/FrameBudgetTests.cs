using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using MajdataPlay.ResourceUpdate;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.ResourceUpdate
{
    /// <summary>Covers the frame slicing of <see cref="FrameBudget"/>.</summary>
    public class FrameBudgetTests
    {
        /// <summary>Checks that work within the budget continues without yielding.</summary>
        [Test]
        public void YieldIfExceededAsync_WithinBudget_CompletesImmediately()
        {
            var budget = new FrameBudget(60_000);

            Assert.That(budget.YieldIfExceededAsync(CancellationToken.None).IsCompleted, Is.True);
        }

        /// <summary>Checks that an exhausted budget yields to a later frame and then completes.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator YieldIfExceededAsync_OverBudget_YieldsAFrame()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var budget = new FrameBudget(0);

                var pending = budget.YieldIfExceededAsync(CancellationToken.None);

                Assert.That(pending.IsCompleted, Is.False);
                await pending;
            });
        }

        /// <summary>Checks that a cancelled token stops the work at the next check.</summary>
        [Test]
        public void YieldIfExceededAsync_Cancelled_Throws()
        {
            var budget = new FrameBudget(60_000);
            using var source = new CancellationTokenSource();
            source.Cancel();

            Assert.Throws<OperationCanceledException>(() => budget.YieldIfExceededAsync(source.Token));
        }
    }
}
