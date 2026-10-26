// iOS static native plug-ins are not dynamically discovered by Unity.
// C# invokes this on Unity's main thread after engine initialization. Avoid
// process constructors, which run too early for Unity-as-a-Library embedding.
#include "IUnityInterface.h"
extern "C" void UnityRegisterPlugin(
    void (UNITY_INTERFACE_API *load)(IUnityInterfaces*),
    void (UNITY_INTERFACE_API *unload)());
extern "C" __attribute__((visibility("default"))) void ffu_register_ios() {
    static bool registered = false;
    if (registered) return;
    UnityRegisterPlugin(UnityPluginLoad, UnityPluginUnload);
    registered = true;
}
