#import <AVFoundation/AVFoundation.h>
#import <CoreMedia/CoreMedia.h>
#import <CoreVideo/CoreVideo.h>
#import <Foundation/Foundation.h>
#import <math.h>
#import "lumenvideo.h"

@interface LCVideoPlayer : NSObject <AVPlayerItemOutputPullDelegate>
@property (nonatomic, strong) AVPlayer *player;
@property (nonatomic, strong) AVPlayerItemVideoOutput *output;
@property (nonatomic) int32_t width;
@property (nonatomic) int32_t height;
@property (nonatomic) int32_t stride;
@property (nonatomic) BOOL loop;
@property (nonatomic) BOOL running;
@property (nonatomic) BOOL observingStatus;
@property (nonatomic) BOOL audioEnabled;
@property (nonatomic) int32_t volumePercent;
@property (nonatomic) Float64 durationSeconds;
@property (nonatomic, strong) id endObserver;
@end

static void ApplyAudio(LCVideoPlayer *ctx);

@implementation LCVideoPlayer
- (void)observeValueForKeyPath:(NSString *)keyPath
                      ofObject:(id)object
                        change:(NSDictionary<NSKeyValueChangeKey,id> *)change
                       context:(void *)context
{
    if ([keyPath isEqualToString:@"status"] &&
        self.player.currentItem.status == AVPlayerItemStatusReadyToPlay)
    {
        ApplyAudio(self);
        [self.player play];
    }
}

- (void)outputMediaDataWillChange:(AVPlayerItemOutput *)sender
{
}
@end

static int32_t Even(int32_t value)
{
    return value & ~1;
}

static LCVideoPlayer *PlayerFrom(lc_video_handle handle)
{
    if (!handle) return nil;
    return (__bridge LCVideoPlayer *)handle;
}

static Float64 SecondsFrom(CMTime time)
{
    if (CMTIME_IS_INVALID(time) || CMTIME_IS_INDEFINITE(time))
        return 0;
    Float64 seconds = CMTimeGetSeconds(time);
    return seconds > 0 && !isnan(seconds) ? seconds : 0;
}

static Float64 BestDurationSeconds(AVURLAsset *asset, AVAssetTrack *track, NSURL *url)
{
    Float64 reported = fmax(SecondsFrom(asset.duration), SecondsFrom(track.timeRange.duration));

    float bps = track.estimatedDataRate;
    NSNumber *size = nil;
    [url getResourceValue:&size forKey:NSURLFileSizeKey error:nil];
    Float64 estimated = 0;
    if (bps > 1000 && size && size.unsignedLongLongValue > 1000)
        estimated = (Float64)size.unsignedLongLongValue * 8.0 / bps;

    // YouTube/fMP4 files often store a ~1/12 container duration. Prefer the bitrate
    // estimate when the container looks far too short for the file size.
    if (estimated > 2 && (reported <= 0 || (reported < 120 && estimated > reported * 3)))
        return estimated;
    return fmax(reported, 0);
}

static Float64 DurationSeconds(LCVideoPlayer *ctx)
{
    if (!ctx) return 0;
    Float64 duration = ctx.durationSeconds;
    Float64 item = SecondsFrom(ctx.player.currentItem.duration);
    if (item > duration)
        duration = item;
    Float64 current = SecondsFrom(ctx.player.currentTime);
    if (current > duration)
        duration = current;
    return duration;
}

lc_video_handle lc_video_open(const char *path, int32_t loop, int32_t audio,
                              int32_t max_width, int32_t max_height)
{
    @autoreleasepool {
        if (!path) return NULL;

        NSString *nsPath = [NSString stringWithUTF8String:path];
        if (nsPath.length == 0) return NULL;

        NSURL *url = [NSURL fileURLWithPath:nsPath];
        AVURLAsset *asset = [AVURLAsset URLAssetWithURL:url options:nil];

        dispatch_semaphore_t loaded = dispatch_semaphore_create(0);
        [asset loadValuesAsynchronouslyForKeys:@[@"tracks", @"duration"] completionHandler:^{
            dispatch_semaphore_signal(loaded);
        }];
        dispatch_semaphore_wait(loaded, dispatch_time(DISPATCH_TIME_NOW, 5 * NSEC_PER_SEC));

        NSError *tracksError = nil;
        if ([asset statusOfValueForKey:@"tracks" error:&tracksError] != AVKeyValueStatusLoaded)
            return NULL;

        NSArray<AVAssetTrack *> *tracks = [asset tracksWithMediaType:AVMediaTypeVideo];
        AVAssetTrack *track = tracks.firstObject;
        if (!track) return NULL;

        CGSize natural = track.naturalSize;
        CGSize display = CGSizeApplyAffineTransform(natural, track.preferredTransform);
        display.width = fabs(display.width);
        display.height = fabs(display.height);
        if (display.width < 1 || display.height < 1)
            display = natural;

        int32_t width = (int32_t)display.width;
        int32_t height = (int32_t)display.height;
        if (max_width > 0 && width > max_width)
        {
            height = (int32_t)lround((double)height * max_width / width);
            width = max_width;
        }
        if (max_height > 0 && height > max_height)
        {
            width = (int32_t)lround((double)width * max_height / height);
            height = max_height;
        }
        width = Even(MAX(width, 2));
        height = Even(MAX(height, 2));

        NSDictionary *attrs = @{
            (id)kCVPixelBufferPixelFormatTypeKey: @(kCVPixelFormatType_32BGRA),
            (id)kCVPixelBufferWidthKey: @(width),
            (id)kCVPixelBufferHeightKey: @(height),
            (id)kCVPixelBufferIOSurfacePropertiesKey: @{}
        };

        AVPlayerItemVideoOutput *output =
            [[AVPlayerItemVideoOutput alloc] initWithPixelBufferAttributes:attrs];
        AVPlayerItem *item = [AVPlayerItem playerItemWithAsset:asset];
        [item addOutput:output];

        AVPlayer *player = [AVPlayer playerWithPlayerItem:item];
        player.actionAtItemEnd = AVPlayerActionAtItemEndNone;
        player.allowsExternalPlayback = NO;

        LCVideoPlayer *ctx = [LCVideoPlayer new];
        ctx.player = player;
        ctx.output = output;
        ctx.width = width;
        ctx.height = height;
        ctx.stride = width * 4;
        ctx.loop = loop != 0;
        ctx.running = YES;
        ctx.audioEnabled = audio != 0;
        ctx.volumePercent = ctx.audioEnabled ? 100 : 0;
        ctx.durationSeconds = BestDurationSeconds(asset, track, url);
        ApplyAudio(ctx);

        [output setDelegate:ctx queue:dispatch_get_main_queue()];
        [output requestNotificationOfMediaDataChangeWithAdvanceInterval:0.03];
        [item addObserver:ctx forKeyPath:@"status" options:NSKeyValueObservingOptionNew context:NULL];
        ctx.observingStatus = YES;

        if (ctx.loop)
        {
            __weak AVPlayer *weakPlayer = player;
            ctx.endObserver = [[NSNotificationCenter defaultCenter]
                addObserverForName:AVPlayerItemDidPlayToEndTimeNotification
                            object:item
                             queue:[NSOperationQueue mainQueue]
                        usingBlock:^(NSNotification *note) {
                            (void)note;
                            AVPlayer *strong = weakPlayer;
                            if (!strong) return;
                            [strong seekToTime:kCMTimeZero
                               toleranceBefore:kCMTimeZero
                                toleranceAfter:kCMTimeZero];
                            [strong play];
                        }];
        }

        [player play];
        return (lc_video_handle)CFBridgingRetain(ctx);
    }
}

int32_t lc_video_is_running(lc_video_handle handle)
{
    LCVideoPlayer *ctx = PlayerFrom(handle);
    return ctx != nil && ctx.running ? 1 : 0;
}

void lc_video_get_info(lc_video_handle handle, lc_video_info *out)
{
    if (!out) return;
    LCVideoPlayer *ctx = PlayerFrom(handle);
    if (!ctx)
    {
        out->width = 0;
        out->height = 0;
        out->stride = 0;
        return;
    }
    out->width = ctx.width;
    out->height = ctx.height;
    out->stride = ctx.stride;
}

int32_t lc_video_copy_frame(lc_video_handle handle, uint8_t *dest,
                            int32_t dest_stride, int32_t dest_height)
{
    if (!dest || dest_stride <= 0 || dest_height <= 0) return 0;
    LCVideoPlayer *ctx = PlayerFrom(handle);
    if (!ctx || !ctx.output) return 0;

    CMTime host = CMClockGetTime(CMClockGetHostTimeClock());
    CMTime time = [ctx.output itemTimeForHostTime:CMTimeGetSeconds(host)];
    if (CMTIME_IS_INVALID(time))
        time = ctx.player.currentItem.currentTime;
    if (CMTIME_IS_INVALID(time)) return 0;

    CVPixelBufferRef buffer = [ctx.output copyPixelBufferForItemTime:time itemTimeForDisplay:NULL];
    if (!buffer) return 0;

    CVPixelBufferLockBaseAddress(buffer, kCVPixelBufferLock_ReadOnly);
    uint8_t *src = (uint8_t *)CVPixelBufferGetBaseAddress(buffer);
    size_t srcStride = CVPixelBufferGetBytesPerRow(buffer);
    size_t srcHeight = CVPixelBufferGetHeight(buffer);
    if (src)
    {
        int32_t rows = (int32_t)MIN((size_t)dest_height, srcHeight);
        int32_t copyBytes = (int32_t)MIN((size_t)dest_stride, srcStride);
        for (int32_t y = 0; y < rows; y++)
            memcpy(dest + ((size_t)y * (size_t)dest_stride), src + (y * srcStride), (size_t)copyBytes);
    }
    CVPixelBufferUnlockBaseAddress(buffer, kCVPixelBufferLock_ReadOnly);
    CVPixelBufferRelease(buffer);
    return src ? 1 : 0;
}

float lc_video_get_position(lc_video_handle handle)
{
    LCVideoPlayer *ctx = PlayerFrom(handle);
    if (!ctx) return 0;
    Float64 duration = DurationSeconds(ctx);
    if (duration <= 0) return 0;
    Float64 current = CMTimeGetSeconds(ctx.player.currentTime);
    if (current < 0) current = 0;
    float position = (float)(current / duration);
    if (position < 0) return 0;
    if (position > 1) return 1;
    return position;
}

void lc_video_set_position(lc_video_handle handle, float position)
{
    LCVideoPlayer *ctx = PlayerFrom(handle);
    if (!ctx) return;
    Float64 duration = DurationSeconds(ctx);
    if (duration <= 0) return;
    if (position < 0) position = 0;
    if (position > 1) position = 1;
    CMTime time = CMTimeMakeWithSeconds(duration * position, 600);
    [ctx.player seekToTime:time toleranceBefore:kCMTimeZero toleranceAfter:kCMTimeZero];
}

int64_t lc_video_get_time_ms(lc_video_handle handle)
{
    LCVideoPlayer *ctx = PlayerFrom(handle);
    if (!ctx) return 0;
    Float64 seconds = CMTimeGetSeconds(ctx.player.currentTime);
    if (seconds < 0 || isnan(seconds)) return 0;
    return (int64_t)llround(seconds * 1000.0);
}

int64_t lc_video_get_length_ms(lc_video_handle handle)
{
    LCVideoPlayer *ctx = PlayerFrom(handle);
    if (!ctx) return 0;
    return (int64_t)llround(DurationSeconds(ctx) * 1000.0);
}

static void ApplyAudio(LCVideoPlayer *ctx)
{
    if (!ctx.player) return;
    ctx.player.allowsExternalPlayback = NO;
    if (!ctx.audioEnabled)
    {
        ctx.player.muted = YES;
        ctx.player.volume = 0;
        return;
    }
    ctx.player.muted = ctx.volumePercent <= 0;
    ctx.player.volume = ctx.volumePercent / 100.0f;
}

int32_t lc_video_get_volume(lc_video_handle handle)
{
    LCVideoPlayer *ctx = PlayerFrom(handle);
    if (!ctx) return 0;
    return ctx.volumePercent;
}

void lc_video_set_volume(lc_video_handle handle, int32_t volume)
{
    LCVideoPlayer *ctx = PlayerFrom(handle);
    if (!ctx || !ctx.audioEnabled) return;
    if (volume < 0) volume = 0;
    if (volume > 100) volume = 100;
    ctx.volumePercent = volume;
    ApplyAudio(ctx);
}

int32_t lc_video_is_paused(lc_video_handle handle)
{
    LCVideoPlayer *ctx = PlayerFrom(handle);
    if (!ctx) return 1;
    return ctx.player.rate == 0 ? 1 : 0;
}

void lc_video_set_paused(lc_video_handle handle, int32_t paused)
{
    LCVideoPlayer *ctx = PlayerFrom(handle);
    if (!ctx) return;
    if (paused)
        [ctx.player pause];
    else
        [ctx.player play];
}

void lc_video_close(lc_video_handle handle)
{
    if (!handle) return;
    @autoreleasepool {
        LCVideoPlayer *ctx = CFBridgingRelease(handle);
        [ctx.player pause];
        if (ctx.observingStatus && ctx.player.currentItem)
        {
            [ctx.player.currentItem removeObserver:ctx forKeyPath:@"status"];
            ctx.observingStatus = NO;
        }
        if (ctx.endObserver)
            [[NSNotificationCenter defaultCenter] removeObserver:ctx.endObserver];
        [ctx.output setDelegate:nil queue:NULL];
        if (ctx.output)
            [ctx.player.currentItem removeOutput:ctx.output];
        ctx.output = nil;
        ctx.player = nil;
        ctx.running = NO;
    }
}
