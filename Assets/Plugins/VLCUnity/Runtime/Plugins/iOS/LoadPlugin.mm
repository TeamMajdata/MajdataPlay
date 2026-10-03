// Register the statically linked bridge before Unity initializes its renderer.
// Decoder module registration is a separate, version-specific bundle source.
#import <UIKit/UIKit.h>
#import <dispatch/dispatch.h>

struct IUnityInterfaces;
using UnityPluginLoadFunc = void (*)(IUnityInterfaces*);
using UnityPluginUnloadFunc = void (*)();
extern "C" void UnityRegisterRenderingPluginV5(UnityPluginLoadFunc, UnityPluginUnloadFunc);
extern "C" void VLCUnity_UnityPluginLoad(IUnityInterfaces*);
extern "C" void VLCUnity_UnityPluginUnload();

@interface VLCUnityPluginLoader : NSObject
@end

@implementation VLCUnityPluginLoader
+ (void)load
{
    [[NSNotificationCenter defaultCenter]
        addObserverForName:UIApplicationDidFinishLaunchingNotification
                    object:nil
                     queue:nil
                usingBlock:^(NSNotification* notification) {
        (void)notification;
        static dispatch_once_t once;
        dispatch_once(&once, ^{
            UnityRegisterRenderingPluginV5(VLCUnity_UnityPluginLoad, VLCUnity_UnityPluginUnload);
        });
    }];
}
@end
