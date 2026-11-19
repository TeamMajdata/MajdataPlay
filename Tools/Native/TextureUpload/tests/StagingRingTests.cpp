// Deterministic tests for the staging ring: slot reuse, Unity's safe frame gating, refusal of unsupported
// textures and the growth rules of the persistent buffer. They need no Vulkan device.
#include "StagingRing.h"

#include <cstdio>
#include <cstring>

namespace
{
int failures = 0;

// Reports a failed expectation without aborting the remaining checks.
void Check(bool condition, const char* expression, int line)
{
    if (condition)
    {
        return;
    }

    std::printf("FAIL line %d: %s\n", line, expression);
    ++failures;
}

#define CHECK(expression) Check((expression), #expression, __LINE__)

// Builds an opaque Unity texture identifier from a small integer so the tests can tell uploads apart.
void* Texture(uintptr_t value)
{
    return reinterpret_cast<void*>(value);
}

// Creates a ring with the default four aligned slots, as the plug-in does for 848x480 RGBA8 frames.
majvku::StagingRing MakeRing()
{
    majvku::StagingRing ring;
    ring.Configure(majvku::StagingRing::RequiredStride(848u * 480u * 4u), majvku::MaxSlots);
    return ring;
}

// Slot reservation returns every slot once and then refuses further uploads.
void TestSlotsAreExhausted()
{
    auto ring = MakeRing();
    CHECK(ring.SlotCount() == majvku::MaxSlots);

    uint64_t offsets[majvku::MaxSlots]{};
    for (int i = 0; i < majvku::MaxSlots; ++i)
    {
        const auto slot = ring.Acquire(Texture(0x1000 + i), 848, 480, &offsets[i]);
        CHECK(slot == i);
    }

    uint64_t offset = 0;
    CHECK(ring.Acquire(Texture(0x2000), 848, 480, &offset) == -1);
    // Every slot starts at a distinct, aligned offset.
    for (int i = 0; i < majvku::MaxSlots; ++i)
    {
        CHECK(offsets[i] == ring.SlotStride() * static_cast<uint64_t>(i));
        CHECK(offsets[i] % majvku::SlotAlignment == 0);
        for (int j = i + 1; j < majvku::MaxSlots; ++j)
        {
            CHECK(offsets[i] != offsets[j]);
        }
    }
}

// A recorded slot is reused only after Unity reports a safe frame that covers the recording frame.
void TestSafeFrameGatesReuse()
{
    auto ring = MakeRing();
    auto* texture = Texture(0x1000);
    uint64_t offset = 0;

    const auto slot = ring.Acquire(texture, 848, 480, &offset);
    CHECK(slot == 0);
    CHECK(ring.Queue(slot));
    CHECK(ring.PendingCount() == 1);

    majvku::PendingUpload upload{};
    CHECK(ring.TakePending(&upload));
    CHECK(upload.slot == slot);
    CHECK(upload.texture == texture);
    CHECK(upload.offset == offset);
    CHECK(ring.PendingCount() == 0);

    ring.MarkRecorded(slot, 100);
    CHECK(!ring.IsReusable(0));
    ring.ObserveSafeFrame(99);
    CHECK(!ring.IsReusable(0));
    ring.ObserveSafeFrame(100);
    CHECK(ring.IsReusable(0));
    // Safe frame numbers never move backwards.
    ring.ObserveSafeFrame(50);
    CHECK(ring.SafeFrame() == 100);
}

// Dropping an upload frees its slot unless the caller asked to refuse the texture.
void TestDroppedUploadsFreeSlots()
{
    auto ring = MakeRing();
    auto* texture = Texture(0x3000);
    uint64_t offset = 0;

    const auto slot = ring.Acquire(texture, 848, 480, &offset);
    CHECK(slot == 0);
    CHECK(ring.Queue(slot));

    majvku::PendingUpload upload{};
    CHECK(ring.TakePending(&upload));
    ring.MarkDropped(upload.slot, upload.texture, false);
    CHECK(ring.IsReusable(0));
    CHECK(!ring.IsRejected(texture));
    CHECK(ring.Acquire(texture, 848, 480, &offset) == 0);
}

// An unsupported image is refused until the caller resets the refusal cache.
void TestRejectedTexturesAreRefused()
{
    auto ring = MakeRing();
    auto* bad = Texture(0x4000);
    auto* other = Texture(0x5000);
    uint64_t offset = 0;

    const auto slot = ring.Acquire(bad, 848, 480, &offset);
    CHECK(slot == 0);
    CHECK(ring.Queue(slot));

    majvku::PendingUpload upload{};
    CHECK(ring.TakePending(&upload));
    ring.MarkDropped(upload.slot, upload.texture, true);
    CHECK(ring.IsRejected(bad));
    CHECK(ring.Acquire(bad, 848, 480, &offset) == -1);
    CHECK(ring.Acquire(other, 848, 480, &offset) == 0);

    ring.ResetRejected();
    CHECK(!ring.IsRejected(bad));
    CHECK(ring.Acquire(bad, 848, 480, &offset) >= 0);
}

// The refusal cache is bounded and evicts its oldest entry.
void TestRefusalCacheIsBounded()
{
    majvku::StagingRing ring;
    for (int i = 0; i < majvku::MaxRejectedTextures + 2; ++i)
    {
        ring.Reject(Texture(0x6000 + i));
    }

    for (int i = 0; i < majvku::MaxRejectedTextures; ++i)
    {
        CHECK(ring.IsRejected(Texture(0x6000 + 2 + i)));
    }
    CHECK(!ring.IsRejected(Texture(0x6000)));
    // Refusing the same texture twice keeps one entry.
    ring.Reject(Texture(0x6000 + 2));
    CHECK(ring.IsRejected(Texture(0x6000 + 2)));
}

// The persistent buffer grows only for frames that no longer fit, and the stride stays aligned.
void TestGrowthRules()
{
    auto small = majvku::StagingRing::RequiredStride(848u * 480u * 4u);
    auto large = majvku::StagingRing::RequiredStride(1920u * 1080u * 4u);
    CHECK(small % majvku::SlotAlignment == 0);
    CHECK(large > small);

    majvku::StagingRing ring;
    ring.Configure(small, majvku::MaxSlots);
    CHECK(!ring.NeedsGrowth(848u * 480u * 4u));
    CHECK(!ring.NeedsGrowth(320u * 240u * 4u));
    CHECK(ring.NeedsGrowth(1920u * 1080u * 4u));

    ring.Configure(large, majvku::MaxSlots);
    CHECK(!ring.NeedsGrowth(1920u * 1080u * 4u));
    CHECK(ring.SlotStride() == large);
}

// Queued uploads are recorded in the order they were enqueued.
void TestPendingQueueIsFifo()
{
    auto ring = MakeRing();
    for (int i = 0; i < 3; ++i)
    {
        uint64_t offset = 0;
        const auto slot = ring.Acquire(Texture(0x7000 + i), 848, 480, &offset);
        CHECK(slot >= 0);
        CHECK(ring.Queue(slot));
    }

    CHECK(ring.PendingCount() == 3);
    for (int i = 0; i < 3; ++i)
    {
        majvku::PendingUpload upload{};
        CHECK(ring.TakePending(&upload));
        CHECK(upload.texture == Texture(0x7000 + i));
    }
    CHECK(!ring.TakePending(nullptr));
    CHECK(ring.PendingCount() == 0);
}

// Dropping queued uploads frees their slots but keeps slots a recorded command buffer still reads.
void TestDropPendingKeepsRecordedSlots()
{
    auto ring = MakeRing();
    uint64_t offset = 0;

    const auto recorded = ring.Acquire(Texture(0x8000), 848, 480, &offset);
    CHECK(recorded == 0);
    CHECK(ring.Queue(recorded));
    majvku::PendingUpload upload{};
    CHECK(ring.TakePending(&upload));
    ring.MarkRecorded(recorded, 7);

    const auto queued = ring.Acquire(Texture(0x8001), 848, 480, &offset);
    CHECK(queued == 1);
    CHECK(ring.Queue(queued));
    CHECK(ring.PendingCount() == 1);

    ring.DropPending();
    CHECK(ring.PendingCount() == 0);
    CHECK(ring.IsReusable(queued));
    CHECK(!ring.IsReusable(recorded));

    ring.ObserveSafeFrame(7);
    CHECK(ring.IsReusable(recorded));
}

// A device shutdown releases every slot and the buffer geometry.
void TestForgetResetsEverything()
{
    auto ring = MakeRing();
    uint64_t offset = 0;
    CHECK(ring.Acquire(Texture(0x9000), 848, 480, &offset) == 0);
    ring.Reject(Texture(0x9001));
    ring.ObserveSafeFrame(42);

    ring.Forget();
    CHECK(!ring.Ready());
    CHECK(ring.SlotStride() == 0);
    CHECK(ring.SafeFrame() == 0);
    CHECK(ring.Acquire(Texture(0x9000), 848, 480, &offset) == -1);
    // Refusals belong to the device that is gone, so the same texture is tried again on the next one.
    CHECK(!ring.IsRejected(Texture(0x9001)));

    ring.Configure(majvku::StagingRing::RequiredStride(848u * 480u * 4u), majvku::MaxSlots);
    CHECK(ring.Acquire(Texture(0x9001), 848, 480, &offset) == 0);
}

// Reset keeps in-flight slots but drops queued uploads and refusals.
void TestResetKeepsInFlightSlots()
{
    auto ring = MakeRing();
    uint64_t offset = 0;

    const auto slot = ring.Acquire(Texture(0x9100), 848, 480, &offset);
    CHECK(slot == 0);
    CHECK(ring.Queue(slot));
    majvku::PendingUpload upload{};
    CHECK(ring.TakePending(&upload));
    ring.MarkRecorded(slot, 3);
    ring.Reject(Texture(0x9101));

    ring.Reset();
    CHECK(!ring.IsReusable(slot));
    CHECK(!ring.IsRejected(Texture(0x9101)));
    CHECK(ring.Acquire(Texture(0x9101), 848, 480, &offset) == 1);
}
} // namespace

int main()
{
    TestSlotsAreExhausted();
    TestSafeFrameGatesReuse();
    TestDroppedUploadsFreeSlots();
    TestRejectedTexturesAreRefused();
    TestRefusalCacheIsBounded();
    TestGrowthRules();
    TestPendingQueueIsFifo();
    TestDropPendingKeepsRecordedSlots();
    TestForgetResetsEverything();
    TestResetKeepsInFlightSlots();

    if (failures != 0)
    {
        std::printf("%d check(s) failed\n", failures);
        return 1;
    }

    std::printf("staging ring tests passed\n");
    return 0;
}
