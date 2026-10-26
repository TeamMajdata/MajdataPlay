using System;
using System.Collections.Generic;
using System.Linq;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    internal static partial class WrapperEmitter
    {
        private sealed partial class Emitter
        {
            /// <summary>Stores copied generic substitutions and CLR inheritance facts for one emission scope.</summary>
            private sealed class GenericVariableScope : Dictionary<string, string>
            {
                /// <summary>Gets every enclosing CLR variable name, including shadowed Java variables.</summary>
                internal HashSet<string> ReservedNames { get; }

                /// <summary>Gets Java variables whose CLR bounds derive from the exception root.</summary>
                internal HashSet<string> ThrowableVariables { get; }

                /// <summary>Copies a lexical scope without sharing mutable metadata with another emitted file.</summary>
                /// <param name="outer">The containing Java-to-CLR substitutions.</param>
                internal GenericVariableScope(IReadOnlyDictionary<string, string> outer)
                    : base(outer.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal), StringComparer.Ordinal)
                {
                    var scope = outer as GenericVariableScope;
                    ReservedNames = new HashSet<string>(scope is null ? outer.Values : scope.ReservedNames, StringComparer.Ordinal);
                    ThrowableVariables = scope is null ? new HashSet<string>(StringComparer.Ordinal)
                        : new HashSet<string>(scope.ThrowableVariables, StringComparer.Ordinal);
                }
            }

            /// <summary>Adds legal C# type parameter names and retained bound classifications to a lexical scope.</summary>
            /// <param name="outer">The outer type variable names and bound metadata.</param>
            /// <param name="parameters">The declared Java type parameters.</param>
            /// <returns>A copied variable scope containing the new parameters.</returns>
            private GenericVariableScope AddVariables(IReadOnlyDictionary<string, string> outer, IReadOnlyList<JavaGenericParameter> parameters)
            {
                var result = new GenericVariableScope(outer);
                foreach (var parameter in parameters)
                {
                    var name = JavaNames.PascalCase(parameter.Name);
                    if (!name.StartsWith("T", StringComparison.Ordinal))
                    {
                        name = "T" + name;
                    }
                    while (!result.ReservedNames.Add(name))
                    {
                        name += "Type";
                    }
                    result[parameter.Name] = name;
                    if (HasThrowableBound(parameter, parameters, outer, new HashSet<string>(StringComparer.Ordinal)))
                    {
                        result.ThrowableVariables.Add(parameter.Name);
                    }
                    else
                    {
                        result.ThrowableVariables.Remove(parameter.Name);
                    }
                }
                return result;
            }

            /// <summary>Resolves exception class bounds transitively through local and enclosing type variables.</summary>
            /// <param name="parameter">The Java variable to classify.</param>
            /// <param name="parameters">The variables declared by the same class or method.</param>
            /// <param name="outer">The enclosing variable scope.</param>
            /// <param name="visited">The variable bounds currently being inspected.</param>
            /// <returns>Whether the variable is represented by a CLR exception wrapper.</returns>
            private bool HasThrowableBound(JavaGenericParameter parameter, IReadOnlyList<JavaGenericParameter> parameters,
                IReadOnlyDictionary<string, string> outer, HashSet<string> visited)
            {
                if (!visited.Add(parameter.Name))
                {
                    return false;
                }
                foreach (var bound in parameter.Bounds)
                {
                    if (bound.Kind == 'L' && _types.TryGetValue(bound.Name, out var declaration) && IsThrowable(declaration))
                    {
                        return true;
                    }
                    if (bound.Kind == 'T')
                    {
                        var local = parameters.FirstOrDefault(value => value.Name == bound.Name);
                        if (local is not null ? HasThrowableBound(local, parameters, outer, visited)
                            : outer is GenericVariableScope scope && scope.ThrowableVariables.Contains(bound.Name))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }

            /// <summary>Checks whether a reference or variable uses the CLR exception inheritance hierarchy.</summary>
            /// <param name="argument">The requested Java generic argument.</param>
            /// <param name="variables">The lexical variable metadata.</param>
            /// <returns>Whether the argument cannot satisfy an unrelated JavaObject class constraint.</returns>
            private bool IsThrowableArgument(JavaGenericType argument, IReadOnlyDictionary<string, string> variables)
            {
                return argument.Kind == 'L' ? _types.TryGetValue(argument.Name, out var declaration) && IsThrowable(declaration)
                    : argument.Kind == 'T' && variables is GenericVariableScope scope && scope.ThrowableVariables.Contains(argument.Name);
            }
        }
    }
}
