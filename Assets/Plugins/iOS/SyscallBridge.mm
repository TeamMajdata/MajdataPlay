#include <pthread.h>
#include <stdint.h>
#include <stdlib.h>

#ifdef __cplusplus
extern "C" {
#endif

int32_t pthread_threadid_np_(void* thread, uint64_t* threadId) {
    return (int32_t)pthread_threadid_np((pthread_t)thread, threadId);
}

void* malloc_(size_t size) {
    return malloc(size);
}

void free_(void* pointer) {
    free(pointer);
}

#ifdef __cplusplus
}
#endif