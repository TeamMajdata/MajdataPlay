namespace MajdataPlay.IO
{
    internal static class OniimaiLayout
    {
        internal static bool PhoneTransform(bool configured)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return OniimaiController.UsePhoneLayout(configured);
#else
            return configured;
#endif
        }
        internal static bool ExternalActive
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return OniimaiController.ExternalActive;
#else
                return false;
#endif
            }
        }
    }
}
