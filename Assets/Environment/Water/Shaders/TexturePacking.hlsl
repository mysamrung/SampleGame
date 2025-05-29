#define BIT32_SIZE 4294967296.0
#define BIT16_SIZE 65535.0


// RGBAの各チャネルをRとGの2チャネルにパックする関数
float2 PackRGBAtoRG(float4 color)
{
    // 各チャネルを0〜255の範囲にスケーリングし、整数に変換
    uint r = (uint)(color.r * BIT16_SIZE);
    uint g = (uint)(color.g * BIT16_SIZE);
    uint b = (uint)(color.b * BIT16_SIZE);
    uint a = (uint)(color.a * BIT16_SIZE);

    // RとGの2チャネルにパック
    float2 packedColor;
    packedColor.r = (float)(r | (g << 16)) / BIT32_SIZE; // 32ビットにパック
    packedColor.g = (float)(b | (a << 16)) / BIT32_SIZE; // 32ビットにパック

    return packedColor;
}

// RGの2チャネルからRGBAの各チャネルに復元する関数
float4 UnpackRGtoRGBA(float2 packedColor)
{
    // 16ビットのRとGを復元
    uint rg = (uint)(packedColor.r * BIT32_SIZE);
    uint r = rg & 0xFFFF;
    uint g = (rg >> 16) & 0xFFFF;

    // 16ビットのBとAを復元
    uint ba = (uint)(packedColor.g * BIT32_SIZE);
    uint b = ba & 0xFFFF;
    uint a = (ba >> 16) & 0xFFFF;

    // RGBAの各チャネルを0〜1の範囲に正規化
    float4 color;
    color.r = (float)r / BIT16_SIZE;
    color.g = (float)g / BIT16_SIZE;
    color.b = (float)b / BIT16_SIZE;
    color.a = (float)a / BIT16_SIZE;

    return color;
}

// RGBAの各チャネルをRにパックする関数
float PackRGBAtoR(float4 color)
{
    // 各チャネルを0〜255の範囲にスケーリングし、整数に変換
    uint r = (uint)(color.r * 255.0f);
    uint g = (uint)(color.g * 255.0f);
    uint b = (uint)(color.b * 255.0f);
    uint a = (uint)(color.a * 255.0f);

    // RとGの2チャネルにパック
    float packedColor = (float)(r | (g << 8) | (b << 16) | (a << 24));
    return packedColor;
}

// Rの2チャネルからRGBAの各チャネルに復元する関数
float4 UnpackRtoRGBA(float packedColor)
{
    // 16ビットのRとGを復元
    uint rgba = (uint)(packedColor);
    uint r = rgba & 0xFF;
    uint g = (rgba >> 8) & 0xFF;
    uint b = (rgba >> 16) & 0xFF;
    uint a = (rgba >> 24) & 0xFF;
    
    return float4(r / 255.0f, g / 255.0f, b / 255.0f, a / 255.0f);
}

