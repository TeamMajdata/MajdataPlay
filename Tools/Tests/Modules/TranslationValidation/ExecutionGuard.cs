using System;
#nullable enable
namespace MajdataPlay.TranslationValidation
{
    /// <summary>
    /// Detects accidental execution of code that should only be inspected as metadata or IL.
    /// </summary>
    internal static class ExecutionGuard
    {
        /// <summary>
        /// Counts fixture constructors, getters, localization calls, and initialization attempts.
        /// </summary>
        private static int s_executionCount;

        /// <summary>
        /// Gets the number of forbidden fixture executions.
        /// </summary>
        internal static int ExecutionCount
        {
            get
            {
                return s_executionCount;
            }
        }

        /// <summary>
        /// Records an execution attempt and creates an exception describing the offending member.
        /// </summary>
        /// <param name="member">The fixture member that was executed.</param>
        /// <returns>An exception that the fixture immediately throws.</returns>
        internal static InvalidOperationException Executed(string member)
        {
            s_executionCount++;
            return new InvalidOperationException($"Static analysis executed fixture code: {member}.");
        }
    }
}
