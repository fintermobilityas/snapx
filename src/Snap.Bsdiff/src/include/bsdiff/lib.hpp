#pragma once

#include <bsdiff.h>
#include <cstdint>

#ifdef SNAP_PLATFORM_WINDOWS
#define SNAP_API __declspec( dllexport )
#define SNAP_CALLING_CONVENTION __cdecl
#elif SNAP_PLATFORM_LINUX
#if defined(__GNUC__)
#define SNAP_API __attribute__((visibility("default")))
#define SNAP_CALLING_CONVENTION 
#endif
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef void (*snap_bsdiff_error_logger_t)(void *opaque, const char *errmsg);

typedef enum _snap_bsdiff_status_type {
  bsdiff_status_type_success = 0,
  bsdiff_status_type_error = 1,
  bsdiff_status_type_invalid_arg = 2,
  bsdiff_status_type_out_of_memory = 3,
  bsdiff_status_type_file_error = 4,
  bsdiff_status_type_end_of_file = 5,
  bsdiff_status_type_corrupt_patch = 6,
  bsdiff_status_type_size_too_large = 7
} snap_bsdiff_status_type;

typedef struct _snap_bsdiff_patch_ctx {
  snap_bsdiff_error_logger_t error_logger;
  const void *older;
  size_t older_size;
  uint8_t **newer;
  size_t newer_size;
  const void *patch;
  const size_t patch_size;
  snap_bsdiff_status_type status;
} snap_bsdiff_patch_ctx;

typedef struct _snap_bsdiff_diff_ctx {
  snap_bsdiff_error_logger_t error_logger;
  const void *older;
  size_t older_size;
  const void *newer;
  size_t newer_size;
  uint8_t **patch;
  size_t patch_size;
  snap_bsdiff_status_type status;
} snap_bsdiff_diff_ctx;

SNAP_API int32_t SNAP_CALLING_CONVENTION snap_bsdiff_patch(snap_bsdiff_patch_ctx *p_ctx);
SNAP_API int32_t SNAP_CALLING_CONVENTION snap_bsdiff_patch_free(snap_bsdiff_patch_ctx* p_ctx);
SNAP_API int32_t SNAP_CALLING_CONVENTION snap_bsdiff_diff(snap_bsdiff_diff_ctx* p_ctx);
SNAP_API int32_t SNAP_CALLING_CONVENTION snap_bsdiff_diff_free(snap_bsdiff_diff_ctx* p_ctx);

#ifdef __cplusplus
}
#endif