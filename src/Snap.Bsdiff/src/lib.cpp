#include "bsdiff/lib.hpp"
#include <cstring>

SNAP_API int32_t SNAP_CALLING_CONVENTION snap_bsdiff_patch(snap_bsdiff_patch_ctx* p_ctx) {
  if(p_ctx == nullptr ||
      p_ctx->older == nullptr ||
      p_ctx->older_size <= 0 ||
      p_ctx->newer != nullptr ||
      p_ctx->newer_size != 0 ||
      p_ctx->patch == nullptr ||
      p_ctx->patch_size <= 0) {
    return 0;
  }

  int ret;
  struct bsdiff_stream oldfile = { nullptr }, newfile = { nullptr }, patchfile = { nullptr };
  struct bsdiff_ctx ctx = { nullptr };
  struct bsdiff_patch_packer packer = { nullptr };

  if ((ret = bsdiff_open_memory_stream(BSDIFF_MODE_READ, p_ctx->older, p_ctx->older_size, &oldfile)) != BSDIFF_SUCCESS) {
    goto cleanup;
  }

  if ((ret = bsdiff_open_memory_stream(BSDIFF_MODE_WRITE, nullptr, 0, &newfile)) != BSDIFF_SUCCESS) {
    goto cleanup;
  }

  if ((ret = bsdiff_open_memory_stream(BSDIFF_MODE_READ, p_ctx->patch, p_ctx->patch_size, &patchfile)) != BSDIFF_SUCCESS) {
    goto cleanup;
  }

  if ((ret = bsdiff_open_bz2_patch_packer(BSDIFF_MODE_READ, &patchfile, &packer)) != BSDIFF_SUCCESS) {
    goto cleanup;
  }

  ctx.log_error = p_ctx->error_logger;

  if ((ret = bspatch(&ctx, &oldfile, &newfile, &packer)) != BSDIFF_SUCCESS) {
    goto cleanup;
  }

cleanup:
  p_ctx->status = static_cast<snap_bsdiff_status_type>(ret);

  if(p_ctx->status == bsdiff_status_type_success) {
    const void* newer_buffer = nullptr;
    size_t newer_buffer_len = 0;
    newfile.get_buffer(newfile.state, &newer_buffer, &newer_buffer_len);

    p_ctx->newer_size = static_cast<int64_t>(newer_buffer_len);
    p_ctx->newer = new uint8_t*[newer_buffer_len];
    std::memcpy(p_ctx->newer, newer_buffer, newer_buffer_len);
  }

  bsdiff_close_patch_packer(&packer);
  bsdiff_close_stream(&patchfile);
  bsdiff_close_stream(&newfile);
  bsdiff_close_stream(&oldfile);

  return p_ctx->status == bsdiff_status_type_success ? 1 : 0;
}

SNAP_API int32_t SNAP_CALLING_CONVENTION snap_bsdiff_patch_free(snap_bsdiff_patch_ctx* p_ctx) {
  if(p_ctx == nullptr) {
    return 0;
  }

  if(p_ctx->newer != nullptr) {
    delete[] p_ctx->newer;
    p_ctx->newer = nullptr;
    p_ctx->newer_size = 0;
  }

  return 1;
}

SNAP_API int32_t SNAP_CALLING_CONVENTION snap_bsdiff_diff(snap_bsdiff_diff_ctx* p_ctx) {
  if(p_ctx == nullptr ||
      p_ctx->older == nullptr ||
      p_ctx->older_size <= 0 ||
      p_ctx->newer == nullptr ||
      p_ctx->newer_size <= 0) {
    return 0;
  }

  int ret;
  struct bsdiff_stream oldfile = { nullptr }, newfile = { nullptr }, patchfile = { nullptr };
  struct bsdiff_ctx ctx = { nullptr };
  struct bsdiff_patch_packer packer = { nullptr };

  if ((ret = bsdiff_open_memory_stream(BSDIFF_MODE_READ, p_ctx->older, p_ctx->older_size, &oldfile)) != BSDIFF_SUCCESS) {
    goto cleanup;
  }

  if ((ret = bsdiff_open_memory_stream(BSDIFF_MODE_READ, p_ctx->newer, p_ctx->newer_size, &newfile)) != BSDIFF_SUCCESS) {
    goto cleanup;
  }

  if ((ret = bsdiff_open_memory_stream(BSDIFF_MODE_WRITE, nullptr, 0, &patchfile)) != BSDIFF_SUCCESS) {
    goto cleanup;
  }

  if ((ret = bsdiff_open_bz2_patch_packer(BSDIFF_MODE_WRITE, &patchfile, &packer)) != BSDIFF_SUCCESS) {
    goto cleanup;
  }

  ctx.log_error = p_ctx->error_logger;

  if ((ret = bsdiff(&ctx, &oldfile, &newfile, &packer)) != BSDIFF_SUCCESS) {
    goto cleanup;
  }

cleanup:
  p_ctx->status = static_cast<snap_bsdiff_status_type>(ret);

  if(p_ctx->status == bsdiff_status_type_success) {
    const void* patch_buffer = nullptr;
    size_t patch_buffer_len = 0;
    patchfile.get_buffer(patchfile.state, &patch_buffer, &patch_buffer_len);

    p_ctx->patch_size = static_cast<int64_t>(patch_buffer_len);
    p_ctx->patch = new uint8_t*[patch_buffer_len];
    std::memcpy(p_ctx->patch, patch_buffer, patch_buffer_len);
  }

  bsdiff_close_patch_packer(&packer);
  bsdiff_close_stream(&patchfile);
  bsdiff_close_stream(&newfile);
  bsdiff_close_stream(&oldfile);

  return p_ctx->status == bsdiff_status_type_success ? 1 : 0;
}

SNAP_API int32_t SNAP_CALLING_CONVENTION snap_bsdiff_diff_free(snap_bsdiff_diff_ctx* p_ctx) {
  if(p_ctx == nullptr) {
    return 0;
  }

  if(p_ctx->patch != nullptr) {
    delete[] p_ctx->patch;
    p_ctx->patch = nullptr;
    p_ctx->patch_size = 0;
  }

  return 1;
}
