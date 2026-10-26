#include <dlfcn.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

/* First-generation prefix documented by curl_version_info(3). */
struct version_info {
    int age;
    const char *version;
    unsigned int version_num;
    const char *host;
    int features;
    const char *ssl_version;
    long ssl_version_num;
    const char *libz_version;
    const char *const *protocols;
};

/* Original managed declaration used a fixed-width Int64 here. */
struct old_version_info {
    int age;
    const char *version;
    unsigned int version_num;
    const char *host;
    int features;
    const char *ssl_version;
    int64_t ssl_version_num;
    const char *libz_version;
    const char *const *protocols;
};

int main(int argc, char **argv) {
    if (argc != 2) return 2;
    void *lib = dlopen(argv[1], RTLD_NOW);
    if (!lib) { puts(dlerror()); return 3; }
    int (*init)(long) = (int (*)(long))dlsym(lib, "curl_global_init");
    struct version_info *(*version)(int) = (struct version_info *(*)(int))dlsym(lib, "curl_version_info");
    void (*cleanup)(void) = (void (*)(void))dlsym(lib, "curl_global_cleanup");
    if (!init || !version || !cleanup || init(3) != 0) return 4;
    struct version_info *info = version(0);
    if (!info || !info->protocols) return 5;
    printf("C long=%zu; correct Protocols offset=%zu; old offset=%zu\n",
        sizeof(long), offsetof(struct version_info, protocols), offsetof(struct old_version_info, protocols));
    if (sizeof(void *) == 4 && info->age >= 1) {
        const void *old_protocols;
        memcpy(&old_protocols, (const char *)info + offsetof(struct old_version_info, protocols), sizeof(old_protocols));
        printf("old Protocols value=%p (read only, never dereferenced)\n", old_protocols);
    }
    printf("curl=%s; host=%s; protocols=", info->version, info->host);
    int http = 0, https = 0;
    for (const char *const *p = info->protocols; *p; ++p) {
        printf("%s ", *p);
        http |= strcmp(*p, "http") == 0;
        https |= strcmp(*p, "https") == 0;
    }
    puts("");
    cleanup();
    dlclose(lib);
    return http && https ? 0 : 6;
}
