// Isolated authored-fixture generator. Windows Media Foundation is a test
// oracle only; this program and its system codecs do not ship with the app.
#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <mferror.h>
#include <wmcodecdsp.h>
#include <wmcontainer.h>
#include <propsys.h>
#include <psapi.h>
#include <wrl/client.h>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <stdexcept>
#include <string>
#include <vector>

using Microsoft::WRL::ComPtr;

static void Require(HRESULT result, const char* operation)
{
    if (FAILED(result)) { std::fprintf(stderr, "%s: 0x%08lx\n", operation, static_cast<unsigned long>(result)); throw std::runtime_error(operation); }
}

static UINT32 Number(const wchar_t* value)
{
    wchar_t* end = nullptr; auto number = std::wcstoul(value, &end, 10);
    if (!*value || *end || number > 1000000) throw std::runtime_error("Invalid fixture parameter");
    return static_cast<UINT32>(number);
}

static void ConfigureLossless(IPropertyStore* settings)
{
    PROPVARIANT enabled{}; enabled.vt = VT_BOOL; enabled.boolVal = VARIANT_TRUE;
    Require(settings->SetValue(MFPKEY_VBRENABLED, enabled), "Enable lossless VBR");
    Require(settings->SetValue(MFPKEY_CONSTRAIN_ENUMERATED_VBRQUALITY, enabled), "Constrain lossless quality");
    PROPVARIANT quality{}; quality.vt = VT_UI4; quality.ulVal = 100;
    Require(settings->SetValue(MFPKEY_DESIRED_VBRQUALITY, quality), "Select lossless quality");
}

static void Generate(const GUID& codec, UINT32 rate, UINT32 channels, UINT32 bits, UINT32 frames)
{
    ComPtr<IMFCollection> types;
    if (codec == MFAudioFormat_WMAudio_Lossless)
    {
        ComPtr<IMFTransform> encoder; ComPtr<IPropertyStore> settings;
        Require(CoCreateInstance(__uuidof(CWMAEncMediaObject), nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&encoder)), "Create lossless encoder");
        Require(encoder.As(&settings), "Read lossless settings");
        ConfigureLossless(settings.Get());
        Require(MFCreateCollection(&types), "Create lossless type list");
        for (DWORD index = 0; index < 4096; ++index)
        {
            ComPtr<IMFMediaType> type; HRESULT result = encoder->GetOutputAvailableType(0, index, &type);
            if (result == MF_E_NO_MORE_TYPES) break;
            Require(result, "Enumerate lossless type"); Require(types->AddElement(type.Get()), "Retain lossless type");
            if (index == 4095) throw std::runtime_error("Too many lossless types");
        }
    }
    else Require(MFTranscodeGetAudioOutputAvailableTypes(codec, MFT_ENUM_FLAG_SYNCMFT | MFT_ENUM_FLAG_LOCALMFT | MFT_ENUM_FLAG_SORTANDFILTER, nullptr, &types), "Enumerate encoder");
    DWORD count = 0; Require(types->GetElementCount(&count), "Count encoder types");
    ComPtr<IMFMediaType> selected;
    UINT32 best = 0;
    for (DWORD index = 0; index < count; ++index)
    {
        ComPtr<IUnknown> item; ComPtr<IMFMediaType> type;
        Require(types->GetElement(index, &item), "Read encoder type"); Require(item.As(&type), "Read media type");
        UINT32 hz = 0, ch = 0, precision = 0, bytes = 0;
        GUID subtype{}; Require(type->GetGUID(MF_MT_SUBTYPE, &subtype), "Read encoded subtype");
        type->GetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, &hz); type->GetUINT32(MF_MT_AUDIO_NUM_CHANNELS, &ch);
        type->GetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, &precision); type->GetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, &bytes);
        if (subtype == codec && hz == rate && ch == channels && precision == bits && (!selected || bytes > best)) { selected = type; best = bytes; }
    }
    if (!selected) throw std::runtime_error("No matching system encoder profile");
    std::printf("Selected: rate=%u channels=%u bits=%u bytesPerSecond=%u\n", rate, channels, bits, best);
    const UINT32 frameBytes = channels * (bits / 8);
    std::vector<BYTE> pcm(static_cast<size_t>(frames) * frameBytes);
    // Distinct, deterministic channel tones plus nonperiodic low-level detail.
    // Retain exact integer source bytes as the independent lossless oracle.
    const double scale = static_cast<double>((1ULL << (bits - 1)) - 1);
    for (UINT32 frame = 0; frame < frames; ++frame)
        for (UINT32 channel = 0; channel < channels; ++channel)
        {
            double tone = .30 * std::sin(6.2831853071795864769 * (233 + 127 * channel) * frame / rate);
            auto value = static_cast<INT32>(std::llround(scale * tone)) + static_cast<INT32>((frame * 13 + channel * 7) % 31) - 15;
            for (UINT32 byte = 0; byte < bits / 8; ++byte)
                pcm[static_cast<size_t>(frame) * frameBytes + channel * (bits / 8) + byte] = static_cast<BYTE>(static_cast<UINT32>(value) >> (8 * byte));
        }
    HANDLE raw = CreateFileW(L"source.pcm", GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (raw == INVALID_HANDLE_VALUE) throw std::runtime_error("Cannot reserve source.pcm");
    DWORD written = 0; const bool saved = WriteFile(raw, pcm.data(), static_cast<DWORD>(pcm.size()), &written, nullptr) != FALSE;
    CloseHandle(raw); if (!saved || written != pcm.size()) throw std::runtime_error("Incomplete source sample write");

    ComPtr<IMFAttributes> attributes;
    Require(MFCreateAttributes(&attributes, 2), "Create sink attributes");
    Require(attributes->SetGUID(MF_TRANSCODE_CONTAINERTYPE, MFTranscodeContainerType_ASF), "Select ASF sink");
    Require(attributes->SetUINT32(MF_SINK_WRITER_DISABLE_THROTTLING, TRUE), "Disable fixture clock throttling");
    ComPtr<IMFByteStream> stream;
    Require(MFCreateFile(MF_ACCESSMODE_WRITE, MF_OPENMODE_FAIL_IF_EXIST, MF_FILEFLAGS_NONE, L"fixture.wma", &stream), "Reserve fixture.wma");
    ComPtr<IMFSinkWriter> writer;
    ComPtr<IMFMediaSink> mediaSink;
    DWORD id = 0;
    if (codec == MFAudioFormat_WMAudio_Lossless)
    {
        // The default sink applies CBR buffering and can silently drop encoded
        // Lossless packets. Configure both the container and its encoder for
        // the same VBR profile; permit bitrate adjustment instead of loss.
        ComPtr<IMFASFContentInfo> content;
        Require(MFCreateASFMediaSink(stream.Get(), &mediaSink), "Create Lossless ASF sink");
        Require(mediaSink.As(&content), "Read ASF content information");
        ComPtr<IMFASFProfile> profile; ComPtr<IMFASFStreamConfig> configuration;
        Require(MFCreateASFProfile(&profile), "Create ASF profile");
        Require(profile->CreateStream(selected.Get(), &configuration), "Create ASF audio configuration");
        Require(configuration->SetStreamNumber(1), "Set ASF stream number");
        Require(profile->SetStream(configuration.Get()), "Set ASF audio stream");
        ComPtr<IPropertyStore> global;
        Require(content->GetEncodingConfigurationPropertyStore(0, &global), "Read ASF global settings");
        PROPVARIANT enabled{}; enabled.vt = VT_BOOL; enabled.boolVal = VARIANT_TRUE;
        Require(global->SetValue(MFPKEY_ASFMEDIASINK_AUTOADJUST_BITRATE, enabled), "Allow ASF bitrate adjustment");
        ComPtr<IPropertyStore> settings;
        Require(content->GetEncodingConfigurationPropertyStore(1, &settings), "Read ASF stream settings");
        ConfigureLossless(settings.Get());
        Require(attributes->SetUnknown(MF_SINK_WRITER_ENCODER_CONFIG, settings.Get()), "Attach matching encoder settings");
        Require(content->SetProfile(profile.Get()), "Set ASF profile");
        Require(MFCreateSinkWriterFromMediaSink(mediaSink.Get(), attributes.Get(), &writer), "Create Lossless writer");
    }
    else
    {
        Require(MFCreateSinkWriterFromURL(nullptr, stream.Get(), attributes.Get(), &writer), "Create ASF writer");
        Require(writer->AddStream(selected.Get(), &id), "Add audio stream");
    }
    ComPtr<IMFMediaType> input; Require(MFCreateMediaType(&input), "Create PCM type");
    Require(input->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio), "Set audio type");
    Require(input->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_PCM), "Set PCM type");
    Require(input->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, channels), "Set channels");
    Require(input->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, rate), "Set sample rate");
    Require(input->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, bits), "Set precision");
    Require(input->SetUINT32(MF_MT_AUDIO_BLOCK_ALIGNMENT, frameBytes), "Set frame bytes");
    Require(input->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, rate * frameBytes), "Set byte rate");
    if (channels > 2)
    {
        // The finite surround fixture is authored in Windows 5.1 order.
        // Compressed enumerated types need not expose the PCM mask attribute.
        if (channels != 6) throw std::runtime_error("No authored layout for this fixture");
        Require(input->SetUINT32(MF_MT_AUDIO_CHANNEL_MASK, 0x3f), "Set authored 5.1 channel mask");
    }
    Require(writer->SetInputMediaType(id, input.Get(), nullptr), "Set PCM input");
    Require(writer->BeginWriting(), "Begin fixture");
    for (UINT32 first = 0; first < frames;)
    {
        const UINT32 batch = std::min<UINT32>(997, frames - first); const UINT32 bytes = batch * frameBytes;
        ComPtr<IMFMediaBuffer> buffer; Require(MFCreateMemoryBuffer(bytes, &buffer), "Create sample buffer");
        BYTE* data = nullptr; Require(buffer->Lock(&data, nullptr, nullptr), "Lock samples");
        std::copy_n(pcm.data() + static_cast<size_t>(first) * frameBytes, bytes, data);
        Require(buffer->Unlock(), "Unlock samples"); Require(buffer->SetCurrentLength(bytes), "Set sample bytes");
        ComPtr<IMFSample> sample; Require(MFCreateSample(&sample), "Create sample"); Require(sample->AddBuffer(buffer.Get()), "Attach samples");
        const LONGLONG start = static_cast<LONGLONG>(first) * 10000000 / rate;
        const LONGLONG end = static_cast<LONGLONG>(first + batch) * 10000000 / rate;
        Require(sample->SetSampleTime(start), "Set sample time"); Require(sample->SetSampleDuration(end - start), "Set sample duration");
        Require(writer->WriteSample(id, sample.Get()), "Write samples"); first += batch;
    }
    Require(writer->Finalize(), "Finalize fixture"); writer.Reset();
    if (mediaSink) Require(mediaSink->Shutdown(), "Shut down ASF sink");
    stream.Reset();
    std::printf("Authored source frames: %u\n", frames);
}

static void Decode(UINT32 expectedRate, UINT32 expectedChannels)
{
    ComPtr<IMFByteStream> stream;
    Require(MFCreateFile(MF_ACCESSMODE_READ, MF_OPENMODE_FAIL_IF_NOT_EXIST, MF_FILEFLAGS_NONE, L"fixture.wma", &stream), "Open authored WMA");
    ComPtr<IMFSourceReader> reader;
    Require(MFCreateSourceReaderFromByteStream(stream.Get(), nullptr, &reader), "Create Windows decoder");
    Require(reader->SetStreamSelection(static_cast<DWORD>(MF_SOURCE_READER_ALL_STREAMS), FALSE), "Disable unselected streams");
    Require(reader->SetStreamSelection(static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM), TRUE), "Select audio");
    ComPtr<IMFMediaType> type; Require(MFCreateMediaType(&type), "Create decoded type");
    Require(type->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio), "Set decoded audio");
    Require(type->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_Float), "Set decoded float");
    Require(type->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, expectedRate), "Preserve decoded rate");
    Require(type->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, expectedChannels), "Preserve decoded channels");
    Require(reader->SetCurrentMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM), nullptr, type.Get()), "Set Windows decoded type");
    Require(reader->GetCurrentMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM), &type), "Read Windows decoded type");
    UINT32 rate = 0, channels = 0, bits = 0;
    Require(type->GetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, &rate), "Read decoded rate");
    Require(type->GetUINT32(MF_MT_AUDIO_NUM_CHANNELS, &channels), "Read decoded channels");
    Require(type->GetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, &bits), "Read decoded precision");
    if (bits != 32 || channels != expectedChannels || rate != expectedRate) throw std::runtime_error("Unexpected decoded format");
    HANDLE raw = CreateFileW(L"windows.f32", GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (raw == INVALID_HANDLE_VALUE) throw std::runtime_error("Cannot reserve Windows decoder output");
    try
    {
        UINT64 total = 0; bool ended = false;
        for (DWORD index = 0; index < 4096; ++index)
        {
            DWORD flags = 0; LONGLONG time = 0; ComPtr<IMFSample> sample;
            Require(reader->ReadSample(static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM), 0, nullptr, &flags, &time, &sample), "Decode Windows sample");
            if (flags & (MF_SOURCE_READERF_ERROR | MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED | MF_SOURCE_READERF_NATIVEMEDIATYPECHANGED))
                throw std::runtime_error("Windows decoder changed its media contract");
            if (sample)
            {
                ComPtr<IMFMediaBuffer> buffer; Require(sample->ConvertToContiguousBuffer(&buffer), "Read decoded buffer");
                BYTE* bytes = nullptr; DWORD length = 0; Require(buffer->Lock(&bytes, nullptr, &length), "Lock decoded buffer");
                DWORD written = 0;
                bool valid = length % (channels * 4) == 0 && total + length <= 16 * 1024 * 1024;
                bool saved = valid && WriteFile(raw, bytes, length, &written, nullptr) != FALSE;
                Require(buffer->Unlock(), "Unlock decoded buffer");
                if (!saved || written != length) throw std::runtime_error("Decoded output exceeds its bounded transport");
                total += length;
                std::printf("Windows sample: time=%lld bytes=%lu\n", time, static_cast<unsigned long>(length));
            }
            if (flags & MF_SOURCE_READERF_ENDOFSTREAM) { ended = true; break; }
        }
        if (!ended || !total) throw std::runtime_error("Windows decoder did not finish bounded input");
        std::printf("Windows decoded: rate=%u channels=%u frames=%llu\n", rate, channels, total / (channels * 4));
        CloseHandle(raw);
    }
    catch (...) { CloseHandle(raw); throw; }
}

int wmain(int argc, wchar_t** argv)
{
    bool com = false, media = false;
    try
    {
        if (argc != 6) throw std::runtime_error("Use: WmaFixtureWriter standard|pro|lossless|voice rate channels bits frames (new repository scratch cwd)");
        const std::wstring name(argv[1]);
        GUID codec = name == L"standard" ? MFAudioFormat_WMAudioV8 : name == L"pro" ? MFAudioFormat_WMAudioV9 :
            name == L"lossless" ? MFAudioFormat_WMAudio_Lossless : name == L"voice" ? MFAudioFormat_MSP1 : GUID_NULL;
        auto rate = Number(argv[2]), channels = Number(argv[3]), bits = Number(argv[4]), frames = Number(argv[5]);
        if (codec == GUID_NULL || rate < 8000 || rate > 96000 || channels < 1 || channels > 8 ||
            (bits != 16 && bits != 24) || !frames || frames > rate * 3) throw std::runtime_error("Fixture parameters exceed the finite matrix");
        Require(CoInitializeEx(nullptr, COINIT_MULTITHREADED), "Initialize COM"); com = true;
        Require(MFStartup(MF_VERSION, MFSTARTUP_FULL), "Initialize media"); media = true;
        Generate(codec, rate, channels, bits, frames);
        Decode(rate, channels);
        HMODULE modules[512]; DWORD used = 0;
        if (!EnumProcessModules(GetCurrentProcess(), modules, sizeof(modules), &used) || used > sizeof(modules))
            throw std::runtime_error("Cannot record system codec modules");
        for (DWORD index = 0; index < used / sizeof(HMODULE); ++index)
        {
            wchar_t path[32768]; DWORD size = GetModuleFileNameW(modules[index], path, 32768);
            if (!size || size >= 32768) throw std::runtime_error("Cannot record module path");
            std::wprintf(L"Module: %ls\n", path);
        }
        MFShutdown(); CoUninitialize(); return 0;
    }
    catch (const std::exception& error)
    {
        std::fprintf(stderr, "%s\n", error.what()); if (media) MFShutdown(); if (com) CoUninitialize(); return 1;
    }
}
