#nullable enable
using System;
using System.Reflection;
using MajdataPlay;
using MajdataPlay.IO;
using MajdataPlay.Settings;
using UnityEngine;

/// <summary>Exercises the production corner updater's resize and delayed-publication lifecycle.</summary>
internal static class CornerUpdaterValidation
{
    /// <summary>Runs corner invalidation checks against small Unity component stand-ins.</summary>
    /// <returns>The number of successful assertions.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a lifecycle expectation fails.</exception>
    internal static int Run()
    {
        var assertions = 0;
        var updater = new RectCornerPositionUpdater();
        var rectangle = updater.Rectangle;
        rectangle.Bounds = new Rect(-5f, 10f, 10f, 4f);
        Screen.width = 1080;
        Screen.height = 1920;
        MajEnv.Settings = new GameSetting();
        var provider = new ScreenPosCanvasLayoutProvider { HasLayout = true, Version = 1 };
        Majdata<ScreenPosCanvasLayoutProvider>.Instance = provider;
        var type = typeof(RectCornerPositionUpdater);
        var awake = type.GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<Action>(updater);
        var lateUpdate = type.GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<Action>(updater);
        awake();
        var order = type.GetCustomAttribute<DefaultExecutionOrder>()!;
        Check(order.Order > 150, "corner publication follows layout provider and appliers", ref assertions);
        for (var i = 0; i < 7; i++)
        {
            lateUpdate();
        }
        Check(rectangle.Reads == 0, "initial settling does not read premature corners", ref assertions);
        lateUpdate();
        Check(rectangle.Reads == 1 && InputManager.SubScreenEdge.y == 14f,
            "first stable frame publishes initialized corners", ref assertions);

        Screen.height = 2400;
        lateUpdate();
        Check(rectangle.Reads == 1, "screen-only resize schedules rather than reading old corners", ref assertions);
        rectangle.Bounds = new Rect(-5f, 12f, 10f, 4f);
        lateUpdate();
        Check(rectangle.Reads == 2 && InputManager.SubScreenEdge.y == 16f,
            "next stable frame updates sub-display input after resize", ref assertions);

        provider.Version++;
        lateUpdate();
        Check(rectangle.Reads == 2, "provider-only layout change is deferred", ref assertions);
        provider.Version++;
        rectangle.Bounds = new Rect(-4f, 15f, 8f, 3f);
        lateUpdate();
        Check(rectangle.Reads == 2, "consecutive layout changes postpone stale publication", ref assertions);
        lateUpdate();
        Check(rectangle.Reads == 3 && InputManager.SubScreenEdge.y == 18f,
            "latest provider layout publishes after it settles", ref assertions);

        MajEnv.Settings.Display.MainScreenScale = 0.8f;
        lateUpdate();
        rectangle.Bounds = new Rect(-4f, 11f, 8f, 3f);
        lateUpdate();
        Check(rectangle.Reads == 4 && InputManager.SubScreenEdge.w == 11f,
            "manual display transform keeps its delayed refresh", ref assertions);

        Screen.width = 0;
        lateUpdate();
        Check(rectangle.Reads == 4, "temporarily unavailable screen dimensions preserve valid bounds", ref assertions);
        Screen.width = 1080;
        lateUpdate();
        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++)
        {
            lateUpdate();
        }
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
        Check(rectangle.Reads == 4, "stable frames do not repeatedly read world corners", ref assertions);
        Check(allocatedBytes == 0, "stable corner invalidation allocates no managed memory", ref assertions);
        return assertions;
    }

    /// <summary>Counts one successful expectation or reports its failure.</summary>
    /// <param name="condition">Whether the expectation passed.</param>
    /// <param name="message">The behavior being validated.</param>
    /// <param name="assertions">The assertion counter to increment.</param>
    /// <exception cref="InvalidOperationException">Thrown when the expectation fails.</exception>
    private static void Check(bool condition, string message, ref int assertions)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
        assertions++;
    }
}
