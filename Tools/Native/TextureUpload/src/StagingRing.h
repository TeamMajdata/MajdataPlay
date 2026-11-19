// Bookkeeping for the plug-in's persistent staging buffer. It owns no Vulkan object and includes no Vulkan
// header, so the slot ring and its reuse rules can be exercised without a graphics device.
#pragma once
#include <cstddef>
#include <cstdint>

namespace majvku
{
// One slot per upload that may be in flight. Four slots absorb the frames Unity keeps between recording a
// command buffer and retiring it; a fifth upload in the same interval is refused so the caller can fall back.
constexpr int MaxSlots = 4;
// Textures whose Vulkan image this plug-in refuses to touch. A refused texture keeps falling back until the
// caller asks for the cache to be reset.
constexpr int MaxRejectedTextures = 4;
// A VkBufferImageCopy needs a texel-aligned buffer offset; 256 bytes also covers the bufferImageGranularity
// of every driver this ships on.
constexpr uint64_t SlotAlignment = 256;

// Rounds a byte count up to a multiple of the slot alignment.
inline uint64_t AlignUp(uint64_t value, uint64_t alignment)
{
    return (value + alignment - 1) / alignment * alignment;
}

// State of one staging slot of the persistent host-visible buffer.
struct SlotState
{
    bool queued = false;     // Set while the slot holds copied pixels waiting for the next render event.
    bool recording = false;  // Set while the render event owns the slot and has not recorded its copy yet.
    bool inFlight = false;   // Set while a recorded Unity command buffer still reads the slot.
    uint64_t frame = 0;      // Unity frame number whose command buffer references the slot while inFlight.
    uint32_t width = 0;      // Width of the pixels copied into the slot.
    uint32_t height = 0;     // Height of the pixels copied into the slot.
    void* texture = nullptr; // Unity texture the slot's pixels are destined for.
    uint64_t offset = 0;     // Byte offset of the slot inside the persistent staging buffer.
};

// An upload copied into a slot that the next render event must record.
struct PendingUpload
{
    void* texture = nullptr; // Unity texture the pixels are destined for.
    uint32_t width = 0;      // Width of the copied pixels.
    uint32_t height = 0;     // Height of the copied pixels.
    uint64_t offset = 0;     // Byte offset of the pixels inside the persistent staging buffer.
    int slot = -1;           // Index of the staging slot that holds the pixels.
};

// Slot ring of the persistent staging buffer and its reuse rules.
// A slot is reused only when it holds no queued or recording upload and Unity reported a safe frame number
// that covers the frame whose command buffer last read it. Nothing here waits for the GPU.
class StagingRing
{
public:
    // Whether a staging buffer geometry has been configured.
    bool Ready() const { return _ready; }
    // Byte stride between two slots of the configured staging buffer.
    uint64_t SlotStride() const { return _slotStride; }
    // Number of slots of the configured staging buffer.
    int SlotCount() const { return _slotCount; }
    // Slot stride a staging buffer needs to hold tightly packed frames of the given size.
    static uint64_t RequiredStride(uint64_t bytes) { return AlignUp(bytes, SlotAlignment); }
    // Whether the configured buffer is too small for frames of the given size.
    bool NeedsGrowth(uint64_t bytes) const { return _slotStride < RequiredStride(bytes); }
    // Newest Unity frame number whose resources Unity reported safe to release.
    uint64_t SafeFrame() const { return _safeFrame; }
    // Number of queued uploads that no render event has recorded yet.
    int PendingCount() const { return _pendingCount; }

    // Records the geometry of a newly created staging buffer and frees every slot.
    void Configure(uint64_t slotStride, int slotCount)
    {
        ResetSlots();
        _pendingCount = 0;
        _slotStride = slotStride;
        _slotCount = slotCount > 0 && slotCount <= MaxSlots ? slotCount : MaxSlots;
        _ready = true;
    }

    // Forgets the staging buffer geometry after Unity's device was shut down or lost. Refusals belong to the
    // device that is gone, so the same texture is tried again on a new one.
    void Forget()
    {
        ResetSlots();
        _pendingCount = 0;
        _rejectedCount = 0;
        _slotStride = 0;
        _slotCount = 0;
        _safeFrame = 0;
        _ready = false;
    }

    // Records a new safe frame number reported by Unity; older numbers are ignored.
    void ObserveSafeFrame(uint64_t safeFrame)
    {
        if (safeFrame > _safeFrame)
        {
            _safeFrame = safeFrame;
        }
    }

    // Whether the slot may receive a new upload without overwriting pixels a recorded command buffer reads.
    bool IsReusable(int index) const
    {
        if (index < 0 || index >= _slotCount)
        {
            return false;
        }
        const auto& slot = _slots[index];
        if (slot.queued || slot.recording)
        {
            return false;
        }
        return !slot.inFlight || _safeFrame >= slot.frame;
    }

    // Whether uploads for the given texture were refused for the rest of this device's life.
    bool IsRejected(const void* texture) const
    {
        for (int i = 0; i < _rejectedCount; ++i)
        {
            if (_rejected[i] == texture)
            {
                return true;
            }
        }
        return false;
    }

    // Refuses further uploads for a texture, evicting the oldest refusal when the cache is full.
    void Reject(const void* texture)
    {
        if (texture == nullptr || IsRejected(texture))
        {
            return;
        }
        if (_rejectedCount == MaxRejectedTextures)
        {
            for (int i = 1; i < _rejectedCount; ++i)
            {
                _rejected[i - 1] = _rejected[i];
            }
            --_rejectedCount;
        }
        _rejected[_rejectedCount++] = texture;
    }

    // Clears every refused texture so a recreated texture is tried again.
    void ResetRejected() { _rejectedCount = 0; }

    // Reserves a staging slot and its byte offset for one upload.
    // Returns the slot index and writes its buffer offset, or -1 when no slot is provably free.
    int Acquire(void* texture, uint32_t width, uint32_t height, uint64_t* offset)
    {
        if (!_ready || IsRejected(texture))
        {
            return -1;
        }
        for (int index = 0; index < _slotCount; ++index)
        {
            if (!IsReusable(index))
            {
                continue;
            }
            auto& slot = _slots[index];
            slot.queued = true;
            slot.recording = false;
            slot.inFlight = false;
            slot.frame = 0;
            slot.width = width;
            slot.height = height;
            slot.texture = texture;
            slot.offset = _slotStride * static_cast<uint64_t>(index);
            if (offset != nullptr)
            {
                *offset = slot.offset;
            }
            return index;
        }
        return -1;
    }

    // Publishes a filled slot to the next render event.
    bool Queue(int index)
    {
        if (index < 0 || index >= _slotCount || _pendingCount >= MaxSlots)
        {
            return false;
        }
        auto& slot = _slots[index];
        if (!slot.queued)
        {
            return false;
        }
        auto& entry = _pending[_pendingCount];
        entry.texture = slot.texture;
        entry.width = slot.width;
        entry.height = slot.height;
        entry.offset = slot.offset;
        entry.slot = index;
        ++_pendingCount;
        return true;
    }

    // Takes the oldest queued upload for recording and marks its slot as recording.
    bool TakePending(PendingUpload* upload)
    {
        if (_pendingCount == 0 || upload == nullptr)
        {
            return false;
        }
        *upload = _pending[0];
        for (int i = 1; i < _pendingCount; ++i)
        {
            _pending[i - 1] = _pending[i];
        }
        --_pendingCount;
        auto& slot = _slots[upload->slot];
        slot.queued = false;
        slot.recording = true;
        return true;
    }

    // Marks a recorded upload's slot as readable by the GPU until its frame becomes safe.
    void MarkRecorded(int index, uint64_t frame)
    {
        if (index < 0 || index >= _slotCount)
        {
            return;
        }
        auto& slot = _slots[index];
        slot.queued = false;
        slot.recording = false;
        slot.inFlight = true;
        slot.frame = frame;
        slot.texture = nullptr;
    }

    // Releases a slot whose upload could not be recorded. `reject` also refuses the texture from now on, which
    // is only correct for an image this plug-in can never copy into.
    void MarkDropped(int index, void* texture, bool reject)
    {
        if (index >= 0 && index < _slotCount)
        {
            _slots[index] = SlotState{};
        }
        if (reject)
        {
            Reject(texture);
        }
    }

    // Drops every queued upload and frees its slot; recorded slots stay in flight.
    void DropPending()
    {
        for (int i = 0; i < _pendingCount; ++i)
        {
            _slots[_pending[i].slot] = SlotState{};
        }
        _pendingCount = 0;
    }

    // Drops queued uploads and refused textures after the caller recreated its texture.
    void Reset()
    {
        DropPending();
        ResetRejected();
    }

private:
    void ResetSlots()
    {
        for (auto& slot : _slots)
        {
            slot = SlotState{};
        }
    }

    SlotState _slots[MaxSlots]{};
    PendingUpload _pending[MaxSlots]{};
    const void* _rejected[MaxRejectedTextures]{};
    int _rejectedCount = 0;
    int _pendingCount = 0;
    int _slotCount = 0;
    uint64_t _slotStride = 0;
    uint64_t _safeFrame = 0;
    bool _ready = false;
};
} // namespace majvku
