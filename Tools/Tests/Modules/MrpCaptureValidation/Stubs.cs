#nullable enable
using System;
using System.Runtime.CompilerServices;

namespace UnityEngine
{
    /// <summary>Models reference identity and Unity's destroyed-object null comparison for managed tests.</summary>
    public sealed class Camera
    {
        /// <summary>Gets or sets whether the simulated native camera has been destroyed.</summary>
        public bool Destroyed { get; set; }

        /// <summary>Compares camera references while treating a destroyed camera as Unity null.</summary>
        /// <param name="left">The first camera or null.</param>
        /// <param name="right">The second camera or null.</param>
        /// <returns>Whether both references identify the same camera or compare equal to Unity null.</returns>
        public static bool operator ==(Camera? left, Camera? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }
            if (ReferenceEquals(left, null))
            {
                return right!.Destroyed;
            }
            if (ReferenceEquals(right, null))
            {
                return left.Destroyed;
            }
            return false;
        }

        /// <summary>Negates Unity-style camera equality.</summary>
        /// <param name="left">The first camera or null.</param>
        /// <param name="right">The second camera or null.</param>
        /// <returns>Whether the references compare unequal.</returns>
        public static bool operator !=(Camera? left, Camera? right)
        {
            return !(left == right);
        }

        /// <summary>Preserves reference identity for dictionary keys, even after native destruction.</summary>
        /// <param name="other">The object to compare.</param>
        /// <returns>Whether the object is this managed camera reference.</returns>
        public override bool Equals(object? other)
        {
            return ReferenceEquals(this, other);
        }

        /// <summary>Gets a stable reference hash independent of destruction.</summary>
        /// <returns>The managed reference identity hash.</returns>
        public override int GetHashCode()
        {
            return RuntimeHelpers.GetHashCode(this);
        }
    }

    /// <summary>Provides the initialization phase used by the production registry.</summary>
    public enum RuntimeInitializeLoadType
    {
        /// <summary>Runs at the start of each Unity runtime session.</summary>
        SubsystemRegistration
    }

    /// <summary>Models Unity's runtime initialization marker without invoking an engine lifecycle.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        /// <summary>Accepts the initialization phase used by production source.</summary>
        /// <param name="loadType">The requested Unity runtime phase.</param>
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType)
        {
            LoadType = loadType;
        }

        /// <summary>Gets the initialization phase requested by the source.</summary>
        public RuntimeInitializeLoadType LoadType { get; }
    }
}

namespace UnityEngine.Rendering
{
    /// <summary>Supplies a small identity token for managed callback argument assertions.</summary>
    public readonly struct RenderTargetIdentifier
    {
        /// <summary>Creates a simulated resolved target identity.</summary>
        /// <param name="value">The stable token identifying the fixture target.</param>
        public RenderTargetIdentifier(int value)
        {
            Value = value;
        }

        /// <summary>Gets the fixture's target identity.</summary>
        public int Value { get; }
    }

    /// <summary>Provides callback identity only; this stub does not record or execute GPU commands.</summary>
    public sealed class CommandBuffer
    {
    }
}
