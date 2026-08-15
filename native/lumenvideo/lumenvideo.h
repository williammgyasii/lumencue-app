#pragma once

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef void *lc_video_handle;

typedef struct lc_video_info {
    int32_t width;
    int32_t height;
    int32_t stride;
} lc_video_info;

lc_video_handle lc_video_open(const char *path, int32_t loop, int32_t audio,
                              int32_t max_width, int32_t max_height);
int32_t lc_video_is_running(lc_video_handle handle);
void lc_video_get_info(lc_video_handle handle, lc_video_info *out);
int32_t lc_video_copy_frame(lc_video_handle handle, uint8_t *dest,
                            int32_t dest_stride, int32_t dest_height);
float lc_video_get_position(lc_video_handle handle);
void lc_video_set_position(lc_video_handle handle, float position);
int64_t lc_video_get_time_ms(lc_video_handle handle);
int64_t lc_video_get_length_ms(lc_video_handle handle);
int32_t lc_video_get_volume(lc_video_handle handle);
void lc_video_set_volume(lc_video_handle handle, int32_t volume);
int32_t lc_video_is_paused(lc_video_handle handle);
void lc_video_set_paused(lc_video_handle handle, int32_t paused);
void lc_video_close(lc_video_handle handle);

#ifdef __cplusplus
}
#endif
