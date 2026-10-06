/* Test oracle only. Build against facebook/openzl at
 * 3cb9dd595836f12a6f8392c8383914ff1ecc45ee. Not linked to the C# package. */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "openzl/zl_compress.h"
#include "openzl/zl_compressor.h"
#include "openzl/zl_decompress.h"
#include "openzl/codecs/zl_store.h"
#include "openzl/codecs/zl_lz4.h"
#include "openzl/codecs/zl_bitpack.h"
#include "openzl/codecs/zl_delta.h"
#include "openzl/codecs/zl_conversion.h"
#include "openzl/codecs/zl_zigzag.h"
#include "openzl/codecs/zl_range_pack.h"
#include "openzl/codecs/zl_constant.h"

int main(int argc, char **argv) {
    if (argc != 6) return 2;
    FILE *f = fopen(argv[2], "rb"); if (!f) return 3;
    fseek(f, 0, SEEK_END); size_t n = ftell(f); rewind(f);
    void *input = malloc(n + 1); if (fread(input, 1, n, f) != n) return 4; fclose(f);
    size_t capacity = 16 * 1024 * 1024 + n * 4;
    void *output = malloc(capacity); ZL_Report result;
    ZL_CCtx *ctx = NULL; ZL_Compressor *compressor = NULL;
    if (!strcmp(argv[1], "decode")) result = ZL_decompress(output, capacity, input, n);
    else {
        ctx = ZL_CCtx_create(); compressor = ZL_Compressor_create();
        ZL_GraphID graph = ZL_GRAPH_STORE;
        if (!strcmp(argv[4], "lz4")) graph = ZL_GRAPH_LZ4;
        if (!strcmp(argv[4], "constant")) graph = ZL_GRAPH_CONSTANT;
        if (!strcmp(argv[4], "numeric") || !strcmp(argv[4], "delta") || !strcmp(argv[4], "zigzag") || !strcmp(argv[4], "range")) {
            graph = ZL_GRAPH_BITPACK;
            if (!strcmp(argv[4], "zigzag")) graph = ZL_Compressor_registerStaticGraph_fromNode1o(compressor, ZL_NODE_ZIGZAG, graph);
            if (!strcmp(argv[4], "delta") || !strcmp(argv[4], "zigzag")) graph = ZL_Compressor_registerStaticGraph_fromNode1o(compressor, ZL_NODE_DELTA_INT, graph);
            if (!strcmp(argv[4], "range")) graph = ZL_Compressor_registerStaticGraph_fromNode1o(compressor, ZL_NODE_RANGE_PACK, graph);
            graph = ZL_Compressor_registerStaticGraph_fromNode1o(compressor, ZL_NODE_CONVERT_SERIAL_TO_NUM_LE32, graph);
        }
        ZL_CCtx_refCompressor(ctx, compressor);
        ZL_CCtx_selectStartingGraphID(ctx, compressor, graph);
        ZL_CCtx_setParameter(ctx, ZL_CParam_formatVersion, atoi(argv[5]));
        ZL_CCtx_setParameter(ctx, ZL_CParam_minStreamSize, -1);
        ZL_CCtx_setParameter(ctx, ZL_CParam_storeOnExpansion, 2);
        result = ZL_CCtx_compress(ctx, output, capacity, input, n);
    }
    if (ZL_isError(result)) { fprintf(stderr, "%s\n", ctx ? ZL_CCtx_getErrorContextString(ctx, result) : "Native decompression failed"); return 5; }
    f = fopen(argv[3], "wb"); if (!f) return 6;
    size_t length = ZL_validResult(result); fwrite(output, 1, length, f); fclose(f);
    ZL_CCtx_free(ctx); ZL_Compressor_free(compressor); free(input); free(output); return 0;
}
