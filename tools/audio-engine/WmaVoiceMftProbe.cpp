// Explicit Windows Voice MFT evaluation. Reads a generated fixture only.
#define NOMINMAX
#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <mferror.h>
#include <wmcodecdsp.h>
#include <psapi.h>
#include <wrl.h>
#include <algorithm>
#include <cstdio>
#include <stdexcept>
#include <vector>
#include <cstring>
#include <cwchar>

using Microsoft::WRL::ComPtr;
static FILE* diagnostics = stdout;

// Read-only COM view over a held snapshot. It has no path or write operation.
class SnapshotStream final : public Microsoft::WRL::RuntimeClass<
    Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>, IStream> {
    HANDLE file_ = INVALID_HANDLE_VALUE;
    ULONGLONG size_ = 0;
    SRWLOCK lock_ = SRWLOCK_INIT;
public:
    ~SnapshotStream() { if (file_ != INVALID_HANDLE_VALUE) CloseHandle(file_); }
    HRESULT Initialize(HANDLE input) {
        LARGE_INTEGER size{};
        if (GetFileType(input) != FILE_TYPE_DISK || !GetFileSizeEx(input, &size) ||
            size.QuadPart <= 0 || size.QuadPart > 16 * 1024 * 1024) return E_INVALIDARG;
        if (!DuplicateHandle(GetCurrentProcess(), input, GetCurrentProcess(), &file_,
            GENERIC_READ, FALSE, 0)) return HRESULT_FROM_WIN32(GetLastError());
        size_ = static_cast<ULONGLONG>(size.QuadPart);
        LARGE_INTEGER zero{};
        return SetFilePointerEx(file_, zero, nullptr, FILE_BEGIN) ? S_OK : HRESULT_FROM_WIN32(GetLastError());
    }
    IFACEMETHODIMP Read(void* bytes, ULONG count, ULONG* read) override {
        if (read) *read = 0;
        if (!bytes && count) return STG_E_INVALIDPOINTER;
        AcquireSRWLockExclusive(&lock_);
        DWORD actual = 0;
        BOOL success = ReadFile(file_, bytes, count, &actual, nullptr);
        HRESULT result = success ? (actual == count ? S_OK : S_FALSE) : HRESULT_FROM_WIN32(GetLastError());
        ReleaseSRWLockExclusive(&lock_);
        if (read) *read = actual;
        return result;
    }
    IFACEMETHODIMP Seek(LARGE_INTEGER distance, DWORD origin, ULARGE_INTEGER* position) override {
        if (origin > STREAM_SEEK_END) return STG_E_INVALIDFUNCTION;
        AcquireSRWLockExclusive(&lock_);
        LARGE_INTEGER actual{};
        BOOL success = SetFilePointerEx(file_, distance, &actual, origin);
        HRESULT result = success ? S_OK : HRESULT_FROM_WIN32(GetLastError());
        ReleaseSRWLockExclusive(&lock_);
        if (success && position) position->QuadPart = static_cast<ULONGLONG>(actual.QuadPart);
        return result;
    }
    IFACEMETHODIMP Stat(STATSTG* result, DWORD flags) override {
        if (!result) return STG_E_INVALIDPOINTER;
        if (flags != STATFLAG_DEFAULT && flags != STATFLAG_NONAME) return STG_E_INVALIDFLAG;
        *result = {}; result->type = STGTY_STREAM; result->cbSize.QuadPart = size_; result->grfMode = STGM_READ;
        return S_OK;
    }
    IFACEMETHODIMP Write(const void*, ULONG, ULONG* written) override {
        if (written) *written = 0;
        return STG_E_ACCESSDENIED;
    }
    IFACEMETHODIMP SetSize(ULARGE_INTEGER) override { return STG_E_ACCESSDENIED; }
    IFACEMETHODIMP CopyTo(IStream*, ULARGE_INTEGER, ULARGE_INTEGER*, ULARGE_INTEGER*) override { return E_NOTIMPL; }
    IFACEMETHODIMP Commit(DWORD) override { return STG_E_ACCESSDENIED; }
    IFACEMETHODIMP Revert() override { return STG_E_ACCESSDENIED; }
    IFACEMETHODIMP LockRegion(ULARGE_INTEGER, ULARGE_INTEGER, DWORD) override { return STG_E_INVALIDFUNCTION; }
    IFACEMETHODIMP UnlockRegion(ULARGE_INTEGER, ULARGE_INTEGER, DWORD) override { return STG_E_INVALIDFUNCTION; }
    IFACEMETHODIMP Clone(IStream** result) override {
        if (!result) return E_POINTER;
        *result = nullptr; return E_NOTIMPL;
    }
};
static void Require(HRESULT result, const char* operation) {
    if (FAILED(result)) {
        std::fprintf(stderr, "%s: 0x%08lx\n", operation, static_cast<unsigned long>(result));
        throw std::runtime_error(operation);
    }
}
static void Pull(IMFTransform* decoder, HANDLE output, bool floating, UINT64& total) {
    MFT_OUTPUT_STREAM_INFO info{};
    Require(decoder->GetOutputStreamInfo(0, &info), "Get output requirements");
    if (info.cbSize > 1024 * 1024 || info.cbAlignment > 4096 ||
        (info.cbAlignment && (info.cbAlignment & (info.cbAlignment - 1))))
        throw std::runtime_error("Output allocation is outside the evaluation bound");
    for (unsigned attempt = 0; attempt < 4096; ++attempt) {
        ComPtr<IMFSample> owned;
        if (!(info.dwFlags & MFT_OUTPUT_STREAM_PROVIDES_SAMPLES)) {
            ComPtr<IMFMediaBuffer> buffer;
            Require(MFCreateSample(&owned), "Create output sample");
            Require(MFCreateAlignedMemoryBuffer(std::max(info.cbSize, 4096UL),
                std::max(info.cbAlignment, 16UL) - 1, &buffer), "Allocate aligned output");
            Require(owned->AddBuffer(buffer.Get()), "Attach output buffer");
        }
        MFT_OUTPUT_DATA_BUFFER data{}; data.pSample = owned.Get(); DWORD status = 0;
        HRESULT result = decoder->ProcessOutput(0, 1, &data, &status);
        if (data.pEvents) data.pEvents->Release();
        if (!owned && data.pSample) owned.Attach(data.pSample);
        if (result == MF_E_TRANSFORM_NEED_MORE_INPUT) return;
        Require(result, "Process decoder output");
        if (!owned || status || (data.dwStatus & MFT_OUTPUT_DATA_BUFFER_FORMAT_CHANGE))
            throw std::runtime_error("Unexpected dynamic decoder output");
        ComPtr<IMFMediaBuffer> buffer;
        Require(owned->ConvertToContiguousBuffer(&buffer), "Collect output");
        BYTE* bytes = nullptr; DWORD length = 0;
        Require(buffer->Lock(&bytes, nullptr, &length), "Lock output");
        DWORD unit = floating ? 4 : 2;
        if (length % unit || length > 1024 * 1024 || total + length / unit > 262144) {
            buffer->Unlock(); throw std::runtime_error("Decoded extent exceeds the evaluation bound");
        }
        std::vector<float> converted;
        if (!floating) {
            converted.resize(length / 2);
            for (size_t i = 0; i < converted.size(); ++i)
                converted[i] = reinterpret_cast<const short*>(bytes)[i] / 32768.0f;
        }
        DWORD saved = 0, outputBytes = length / unit * 4;
        BOOL success = WriteFile(output, floating ? bytes : reinterpret_cast<const BYTE*>(converted.data()), outputBytes, &saved, nullptr);
        buffer->Unlock();
        if (!success || saved != outputBytes) throw std::runtime_error("Cannot save decoded samples");
        LONGLONG time = -1, duration = -1; owned->GetSampleTime(&time); owned->GetSampleDuration(&duration);
        std::fprintf(diagnostics, "Output: frames=%lu time=%lld duration=%lld\n", outputBytes / 4, time, duration);
        total += outputBytes / 4;
    }
    throw std::runtime_error("Decoder output did not reach a bounded input boundary");
}
static void Decode(const wchar_t* input, bool inherited) {
    ComPtr<IMFByteStream> stream;
    if (inherited) {
        BOOL inJob = FALSE;
        if (!IsProcessInJob(GetCurrentProcess(), nullptr, &inJob) || !inJob)
            throw std::runtime_error("Inherited decoding requires a parent-owned job");
        auto snapshot = Microsoft::WRL::Make<SnapshotStream>();
        if (!snapshot) throw std::bad_alloc();
        Require(snapshot->Initialize(GetStdHandle(STD_INPUT_HANDLE)), "Open inherited snapshot");
        Require(MFCreateMFByteStreamOnStream(snapshot.Get(), &stream), "Wrap inherited snapshot");
    } else {
        Require(MFCreateFile(MF_ACCESSMODE_READ, MF_OPENMODE_FAIL_IF_NOT_EXIST, MF_FILEFLAGS_NONE, input, &stream), "Open source");
    }
    QWORD size = 0; Require(stream->GetLength(&size), "Read source size");
    if (!size || size > 16 * 1024 * 1024) throw std::runtime_error("Source exceeds evaluation limit");
    ComPtr<IMFSourceReader> reader;
    Require(MFCreateSourceReaderFromByteStream(stream.Get(), nullptr, &reader), "Create compressed reader");
    Require(reader->SetStreamSelection(static_cast<DWORD>(MF_SOURCE_READER_ALL_STREAMS), FALSE), "Deselect streams");
    Require(reader->SetStreamSelection(static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM), TRUE), "Select audio");
    ComPtr<IMFMediaType> type;
    Require(reader->GetNativeMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM), 0, &type), "Read compressed type");
    GUID subtype{}; UINT32 rate = 0, channels = 0;
    Require(type->GetGUID(MF_MT_SUBTYPE, &subtype), "Read subtype");
    Require(type->GetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, &rate), "Read rate");
    Require(type->GetUINT32(MF_MT_AUDIO_NUM_CHANNELS, &channels), "Read channels");
    if (subtype != MFAudioFormat_MSP1 || channels != 1 ||
        (rate != 8000 && rate != 11025 && rate != 16000 && rate != 22050))
        throw std::runtime_error("Input is outside the Voice evaluation profiles");
    Require(reader->SetCurrentMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM), nullptr, type.Get()), "Keep compressed reader output");
    ComPtr<IMFTransform> decoder;
    Require(CoCreateInstance(__uuidof(CWMSPDecMediaObject), nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&decoder)), "Create explicit Voice decoder");
    DWORD inputs = 0, outputs = 0; Require(decoder->GetStreamCount(&inputs, &outputs), "Get stream counts");
    if (inputs != 1 || outputs != 1) throw std::runtime_error("Unexpected decoder stream count");
    Require(decoder->SetInputType(0, type.Get(), 0), "Set encoded input type");
    bool floating = true;
    for (unsigned choice = 0; choice < 2; ++choice) {
        WAVEFORMATEX wave{}; wave.wFormatTag = choice ? WAVE_FORMAT_PCM : WAVE_FORMAT_IEEE_FLOAT;
        wave.nChannels = 1; wave.nSamplesPerSec = rate; wave.wBitsPerSample = choice ? 16 : 32;
        wave.nBlockAlign = wave.wBitsPerSample / 8; wave.nAvgBytesPerSec = rate * wave.nBlockAlign;
        ComPtr<IMFMediaType> outputType; Require(MFCreateMediaType(&outputType), "Create output type");
        Require(MFInitMediaTypeFromWaveFormatEx(outputType.Get(), &wave, sizeof(wave)), "Describe output type");
        HRESULT result = decoder->SetOutputType(0, outputType.Get(), 0);
        std::fprintf(diagnostics, "Output type: bits=%u result=0x%08lx\n", wave.wBitsPerSample, static_cast<unsigned long>(result));
        if (SUCCEEDED(result)) { floating = choice == 0; break; }
        if (choice) Require(result, "Set decoder output type");
    }
    Require(decoder->ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, 0), "Begin stream");
    Require(decoder->ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0), "Start stream");
    HANDLE output = inherited ? GetStdHandle(STD_OUTPUT_HANDLE) :
        CreateFileW(L"decoded.f32", GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (output == INVALID_HANDLE_VALUE) throw std::runtime_error("Cannot create output");
    UINT64 total = 0;
    try {
        if (inherited) {
            // Little-endian x64 evaluation wire header, followed by mono float32.
            // A successful child exit and nonempty complete samples are required.
            DWORD header[6]{}; std::memcpy(header, "CSVOICE1", 8);
            header[2] = rate; header[3] = 1; header[4] = 32; header[5] = floating ? 32 : 16;
            DWORD saved = 0;
            if (!WriteFile(output, header, sizeof(header), &saved, nullptr) || saved != sizeof(header))
                throw std::runtime_error("Cannot write sample header");
        }
        bool ended = false;
        for (unsigned count = 0; count < 4096; ++count) {
            DWORD flags = 0; LONGLONG time = 0; ComPtr<IMFSample> sample;
            Require(reader->ReadSample(static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM), 0, nullptr, &flags, &time, &sample), "Read compressed sample");
            if (flags & ~MF_SOURCE_READERF_ENDOFSTREAM) throw std::runtime_error("Unexpected source stream transition");
            if (sample) {
                Require(decoder->ProcessInput(0, sample.Get(), 0), "Process encoded input");
                Pull(decoder.Get(), output, floating, total);
            }
            if (flags & MF_SOURCE_READERF_ENDOFSTREAM) { ended = true; break; }
        }
        if (!ended) throw std::runtime_error("Source exceeded packet bound");
        Require(decoder->ProcessMessage(MFT_MESSAGE_NOTIFY_END_OF_STREAM, 0), "End input");
        Require(decoder->ProcessMessage(MFT_MESSAGE_COMMAND_DRAIN, 0), "Drain decoder");
        Pull(decoder.Get(), output, floating, total);
        Require(decoder->ProcessMessage(MFT_MESSAGE_NOTIFY_END_STREAMING, 0), "End stream");
        if (!total) throw std::runtime_error("Decoder produced no audio");
        std::fprintf(diagnostics, "Decoded: rate=%u frames=%llu format=%s\n", rate, total, floating ? "float32" : "pcm16");
        if (!inherited) CloseHandle(output);
    } catch (...) { if (!inherited) CloseHandle(output); throw; }
}
int wmain(int count, wchar_t** args) {
    if (count != 2) return 2;
    const bool inherited = std::wcscmp(args[1], L"--inherited") == 0;
    if (inherited) diagnostics = stderr;
    bool com = false, media = false;
    try {
        if (!SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32))
            throw std::runtime_error("Cannot restrict DLL search to Windows");
        Require(CoInitializeEx(nullptr, COINIT_MULTITHREADED), "Initialize COM"); com = true;
        Require(MFStartup(MF_VERSION, MFSTARTUP_FULL), "Start Media Foundation"); media = true;
        Decode(args[1], inherited);
        HMODULE modules[512]; DWORD used = 0;
        if (!EnumProcessModules(GetCurrentProcess(), modules, sizeof(modules), &used) || used > sizeof(modules))
            throw std::runtime_error("Cannot record modules");
        for (DWORD i = 0; i < used / sizeof(HMODULE); ++i) {
            wchar_t path[32768]; DWORD length = GetModuleFileNameW(modules[i], path, 32768);
            if (!length || length >= 32768) throw std::runtime_error("Cannot record module path");
            std::fwprintf(diagnostics, L"Module: %ls\n", path);
        }
        MFShutdown(); CoUninitialize(); return 0;
    } catch (const std::exception& error) {
        std::fprintf(stderr, "%s\n", error.what());
        if (media) MFShutdown(); if (com) CoUninitialize(); return 1;
    }
}
