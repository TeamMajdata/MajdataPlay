#pragma once

struct libvlc_media_player_t;
using PortableAndroidSetContext = void (*)(libvlc_media_player_t*, void*);

// The Java helper must outlive every MediaCodec callback, including callbacks
// joined by libvlc_media_player_release. Missing helpers only disable this
// optional hardware-decoder integration; GPU presentation remains available.
void PortableAndroidAttachPlayer(libvlc_media_player_t*, PortableAndroidSetContext);
void PortableAndroidReleasePlayer(libvlc_media_player_t*);
