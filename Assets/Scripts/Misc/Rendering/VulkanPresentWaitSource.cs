#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
#nullable enable
namespace MajdataPlay.Rendering
{
    /// <summary>
    /// Reads Vulkan presentation progress from the MajdataPlayVulkanPacing native layer (see
    /// <see cref="VulkanPacingLayer"/>), which tags every presentation with an id when Unity's device has
    /// VK_KHR_present_id and VK_KHR_present_wait.
    /// </summary>
    internal sealed class VulkanPresentWaitSource : IPresentWaitSource
    {
        /// <inheritdoc/>
        public bool TryQuery(out ulong generation, out ulong presentId)
        {
            return VulkanPacingLayer.Native.PresentWaitQuery(out generation, out presentId) == 1;
        }

        /// <inheritdoc/>
        public int Poll(ulong generation, ulong presentId)
        {
            return VulkanPacingLayer.Native.PresentWaitPoll(generation, presentId);
        }
    }
}
#endif
