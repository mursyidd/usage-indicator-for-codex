#define WINAPI __stdcall
#define TRUE 1
#define FALSE 0
#define NULL ((void *)0)
#define INFINITE 0xffffffffUL
#define WAIT_OBJECT_0 0UL
#define STD_INPUT_HANDLE ((DWORD)-10)
#define STD_OUTPUT_HANDLE ((DWORD)-11)
#define STD_ERROR_HANDLE ((DWORD)-12)
#define STARTF_USESTDHANDLES 0x00000100UL
#define DUPLICATE_SAME_ACCESS 0x00000002UL
#define GENERIC_READ 0x80000000UL
#define FILE_SHARE_READ 0x00000001UL
#define FILE_SHARE_WRITE 0x00000002UL
#define OPEN_EXISTING 3UL
#define FILE_ATTRIBUTE_NORMAL 0x00000080UL
#define STANDARD_HANDLES_UNAVAILABLE 0
#define STANDARD_HANDLES_READY 1
#define STANDARD_HANDLES_ERROR -1
#define SW_HIDE 0
#define HEAP_ZERO_MEMORY 0x00000008UL
#define ERROR_ALREADY_EXISTS 183UL
#define CSTR_EQUAL 2
#define VS_FIXEDFILEINFO_SIGNATURE 0xFEEF04BDUL
#ifdef USAGE_INDICATOR_E2E_TEST
#define INTEGRATION_LOCAL_APP_DATA_CAPACITY 32768UL
#endif

typedef unsigned short WORD;
typedef unsigned long DWORD;
typedef long LONG;
typedef LONG HRESULT;
typedef int BOOL;
typedef unsigned __int64 SIZE_T;
typedef void *HANDLE;
typedef void *HWND;
typedef unsigned short WCHAR;
typedef WCHAR *LPWSTR;
typedef const WCHAR *LPCWSTR;
#define INVALID_HANDLE_VALUE ((HANDLE)(SIZE_T)-1)

typedef struct STARTUPINFOW_TAG
{
    DWORD cb;
    LPWSTR lpReserved;
    LPWSTR lpDesktop;
    LPWSTR lpTitle;
    DWORD dwX;
    DWORD dwY;
    DWORD dwXSize;
    DWORD dwYSize;
    DWORD dwXCountChars;
    DWORD dwYCountChars;
    DWORD dwFillAttribute;
    DWORD dwFlags;
    WORD wShowWindow;
    WORD cbReserved2;
    unsigned char *lpReserved2;
    HANDLE hStdInput;
    HANDLE hStdOutput;
    HANDLE hStdError;
} STARTUPINFOW;

typedef struct PROCESS_INFORMATION_TAG
{
    HANDLE hProcess;
    HANDLE hThread;
    DWORD dwProcessId;
    DWORD dwThreadId;
} PROCESS_INFORMATION;

typedef struct GUID_TAG
{
    DWORD Data1;
    WORD Data2;
    WORD Data3;
    unsigned char Data4[8];
} GUID;

typedef struct VS_FIXEDFILEINFO_TAG
{
    DWORD dwSignature;
    DWORD dwStrucVersion;
    DWORD dwFileVersionMS;
    DWORD dwFileVersionLS;
    DWORD dwProductVersionMS;
    DWORD dwProductVersionLS;
    DWORD dwFileFlagsMask;
    DWORD dwFileFlags;
    DWORD dwFileOS;
    DWORD dwFileType;
    DWORD dwFileSubtype;
    DWORD dwFileDateMS;
    DWORD dwFileDateLS;
} VS_FIXEDFILEINFO;

typedef struct PRODUCT_VERSION_TAG
{
    DWORD major;
    DWORD minor;
    DWORD patch;
} PRODUCT_VERSION;

__declspec(dllimport) LPWSTR WINAPI GetCommandLineW(void);
__declspec(dllimport) LPWSTR *WINAPI CommandLineToArgvW(LPCWSTR commandLine, int *argumentCount);
__declspec(dllimport) HANDLE WINAPI LocalFree(HANDLE memory);
__declspec(dllimport) DWORD WINAPI GetModuleFileNameW(HANDLE module, LPWSTR fileName, DWORD size);
#ifdef USAGE_INDICATOR_E2E_TEST
__declspec(dllimport) DWORD WINAPI GetEnvironmentVariableW(
    LPCWSTR name,
    LPWSTR buffer,
    DWORD size);
#endif
__declspec(dllimport) HANDLE WINAPI GetProcessHeap(void);
__declspec(dllimport) void *WINAPI HeapAlloc(HANDLE heap, DWORD flags, SIZE_T bytes);
__declspec(dllimport) BOOL WINAPI HeapFree(HANDLE heap, DWORD flags, void *memory);
__declspec(dllimport) BOOL WINAPI CreateProcessW(
    LPCWSTR applicationName,
    LPWSTR commandLine,
    void *processAttributes,
    void *threadAttributes,
    BOOL inheritHandles,
    DWORD creationFlags,
    void *environment,
    LPCWSTR currentDirectory,
    STARTUPINFOW *startupInfo,
    PROCESS_INFORMATION *processInformation);
__declspec(dllimport) HANDLE WINAPI CreateFileW(
    LPCWSTR fileName,
    DWORD desiredAccess,
    DWORD shareMode,
    void *securityAttributes,
    DWORD creationDisposition,
    DWORD flagsAndAttributes,
    HANDLE templateFile);
__declspec(dllimport) HANDLE WINAPI GetCurrentProcess(void);
__declspec(dllimport) DWORD WINAPI GetCurrentProcessId(void);
__declspec(dllimport) DWORD WINAPI GetLastError(void);
__declspec(dllimport) BOOL WINAPI DuplicateHandle(
    HANDLE sourceProcess,
    HANDLE sourceHandle,
    HANDLE targetProcess,
    HANDLE *targetHandle,
    DWORD desiredAccess,
    BOOL inheritHandle,
    DWORD options);
__declspec(dllimport) DWORD WINAPI WaitForSingleObject(HANDLE handle, DWORD milliseconds);
__declspec(dllimport) BOOL WINAPI GetExitCodeProcess(HANDLE process, DWORD *exitCode);
__declspec(dllimport) BOOL WINAPI CloseHandle(HANDLE handle);
__declspec(dllimport) void WINAPI ExitProcess(DWORD exitCode);
__declspec(dllimport) DWORD WINAPI GetConsoleProcessList(DWORD *processList, DWORD processCount);
__declspec(dllimport) HWND WINAPI GetConsoleWindow(void);
__declspec(dllimport) BOOL WINAPI ShowWindow(HWND window, int command);
__declspec(dllimport) HANDLE WINAPI GetStdHandle(DWORD standardHandle);
__declspec(dllimport) BOOL WINAPI WriteFile(
    HANDLE file,
    const void *buffer,
    DWORD bytesToWrite,
    DWORD *bytesWritten,
    void *overlapped);
__declspec(dllimport) BOOL WINAPI CreateDirectoryW(LPCWSTR path, void *securityAttributes);
__declspec(dllimport) BOOL WINAPI CopyFileW(LPCWSTR existingFile, LPCWSTR newFile, BOOL failIfExists);
__declspec(dllimport) int WINAPI CompareStringOrdinal(
    LPCWSTR string1,
    int count1,
    LPCWSTR string2,
    int count2,
    BOOL ignoreCase);
__declspec(dllimport) HRESULT WINAPI SHGetKnownFolderPath(
    const GUID *folderId,
    DWORD flags,
    HANDLE token,
    LPWSTR *path);
__declspec(dllimport) void WINAPI CoTaskMemFree(void *memory);
__declspec(dllimport) DWORD WINAPI GetFileVersionInfoSizeW(LPCWSTR fileName, DWORD *handle);
__declspec(dllimport) BOOL WINAPI GetFileVersionInfoW(
    LPCWSTR fileName,
    DWORD handle,
    DWORD length,
    void *data);
__declspec(dllimport) BOOL WINAPI VerQueryValueW(
    const void *block,
    LPCWSTR subBlock,
    void **buffer,
    unsigned int *length);

static const GUID FolderIdLocalAppData =
{
    0xF1B32785UL,
    0x6FBA,
    0x4FCF,
    {0x9D, 0x55, 0x7B, 0x8E, 0x7F, 0x15, 0x70, 0x91}
};
static const WCHAR GuiRelativePath[] = L"app\\UsageIndicatorForCodex.Gui.exe";
static const WCHAR UpdateHostRelativePath[] = L"updater\\UsageIndicatorForCodex.UpdateHost.exe";
static const WCHAR UpdateCacheProductDirectory[] = L"UsageIndicatorForCodex";
static const WCHAR UpdateCacheDirectory[] = L"update-host";
static const WCHAR UpdateCacheFilePrefix[] = L"UsageIndicatorForCodex.UpdateHost.";
static const WCHAR ExecutableSuffix[] = L".exe";
#ifdef USAGE_INDICATOR_E2E_TEST
static const WCHAR IntegrationLocalAppDataVariable[] =
    L"USAGE_INDICATOR_E2E_LOCAL_APP_DATA";
#endif
static const WCHAR DefaultArgument[] = L"help";
static const WCHAR AsyncArgument[] = L"start";
static const WCHAR CheckUpdateArgument[] = L"check-update";
static const WCHAR UpdateArgument[] = L"update";
static const WCHAR CommandOption[] = L"--command";
static const WCHAR InstallRootOption[] = L"--install-root";
static const WCHAR BootstrapVersionOption[] = L"--bootstrap-version";
static const WCHAR BootstrapVersion[] = L"1";
static const WCHAR NullDeviceName[] = L"NUL";
static const WCHAR VersionRootQuery[] = L"\\";
static const char GuiLaunchFailureMessage[] =
    "usage-indicator.exe could not start UsageIndicatorForCodex.Gui.exe.\r\n";
static const char UpdateHostLaunchFailureMessage[] =
    "usage-indicator.exe could not prepare or start the cached update host.\r\n";

static SIZE_T StringLength(LPCWSTR value)
{
    SIZE_T length = 0;
    while (value[length] != L'\0')
    {
        ++length;
    }

    return length;
}

static BOOL StringsEqual(LPCWSTR left, LPCWSTR right)
{
    SIZE_T index = 0;
    while (left[index] == right[index])
    {
        if (left[index] == L'\0')
        {
            return TRUE;
        }

        ++index;
    }

    return FALSE;
}

static void ZeroBytes(void *value, SIZE_T byteCount)
{
    volatile unsigned char *bytes = (volatile unsigned char *)value;
    SIZE_T index;
    for (index = 0; index < byteCount; ++index)
    {
        bytes[index] = 0;
    }
}

static LPWSTR DuplicateString(HANDLE heap, LPCWSTR value, SIZE_T length)
{
    SIZE_T index;
    LPWSTR copy = (LPWSTR)HeapAlloc(heap, 0, (length + 1) * sizeof(WCHAR));
    if (copy == NULL)
    {
        return NULL;
    }

    for (index = 0; index < length; ++index)
    {
        copy[index] = value[index];
    }

    copy[length] = L'\0';
    return copy;
}

static LPWSTR GetLauncherPath(HANDLE heap)
{
    DWORD capacity = 260;
    for (;;)
    {
        LPWSTR path = (LPWSTR)HeapAlloc(heap, 0, (SIZE_T)capacity * sizeof(WCHAR));
        DWORD length;
        if (path == NULL)
        {
            return NULL;
        }

        length = GetModuleFileNameW(NULL, path, capacity);
        if (length > 0 && length < capacity)
        {
            return path;
        }

        HeapFree(heap, 0, path);
        if (capacity > 32768)
        {
            return NULL;
        }

        capacity *= 2;
    }
}

static LPWSTR GetParentDirectory(HANDLE heap, LPCWSTR path)
{
    SIZE_T length = StringLength(path);
    while (length > 0 && path[length - 1] != L'\\' && path[length - 1] != L'/')
    {
        --length;
    }

    while (length > 0 && (path[length - 1] == L'\\' || path[length - 1] == L'/'))
    {
        --length;
    }

    if (length == 0)
    {
        return NULL;
    }

    return DuplicateString(heap, path, length);
}

static LPWSTR JoinPath(HANDLE heap, LPCWSTR left, LPCWSTR right)
{
    SIZE_T leftLength = StringLength(left);
    SIZE_T rightLength = StringLength(right);
    SIZE_T index;
    BOOL needsSeparator = leftLength > 0
        && left[leftLength - 1] != L'\\'
        && left[leftLength - 1] != L'/';
    LPWSTR path = (LPWSTR)HeapAlloc(
        heap,
        0,
        (leftLength + (needsSeparator ? 1 : 0) + rightLength + 1) * sizeof(WCHAR));
    if (path == NULL)
    {
        return NULL;
    }

    for (index = 0; index < leftLength; ++index)
    {
        path[index] = left[index];
    }

    if (needsSeparator)
    {
        path[leftLength++] = L'\\';
    }

    for (index = 0; index < rightLength; ++index)
    {
        path[leftLength + index] = right[index];
    }

    path[leftLength + rightLength] = L'\0';
    return path;
}

static SIZE_T AppendQuotedArgument(LPWSTR destination, SIZE_T offset, LPCWSTR argument)
{
    SIZE_T index = 0;
    SIZE_T backslashCount = 0;
    SIZE_T repeat;

    destination[offset++] = L'"';
    while (argument[index] != L'\0')
    {
        if (argument[index] == L'\\')
        {
            ++backslashCount;
            ++index;
            continue;
        }

        if (argument[index] == L'"')
        {
            for (repeat = 0; repeat < backslashCount * 2 + 1; ++repeat)
            {
                destination[offset++] = L'\\';
            }

            destination[offset++] = L'"';
            backslashCount = 0;
            ++index;
            continue;
        }

        for (repeat = 0; repeat < backslashCount; ++repeat)
        {
            destination[offset++] = L'\\';
        }

        backslashCount = 0;
        destination[offset++] = argument[index++];
    }

    for (repeat = 0; repeat < backslashCount * 2; ++repeat)
    {
        destination[offset++] = L'\\';
    }

    destination[offset++] = L'"';
    return offset;
}

static LPWSTR BuildCommandLine(
    HANDLE heap,
    LPCWSTR executablePath,
    int argumentCount,
    LPCWSTR *arguments)
{
    SIZE_T characterCapacity = StringLength(executablePath) * 2 + 3;
    SIZE_T offset = 0;
    int argumentIndex;
    LPWSTR commandLine;

    for (argumentIndex = 0; argumentIndex < argumentCount; ++argumentIndex)
    {
        characterCapacity += StringLength(arguments[argumentIndex]) * 2 + 4;
    }

    commandLine = (LPWSTR)HeapAlloc(
        heap,
        HEAP_ZERO_MEMORY,
        characterCapacity * sizeof(WCHAR));
    if (commandLine == NULL)
    {
        return NULL;
    }

    offset = AppendQuotedArgument(commandLine, offset, executablePath);
    for (argumentIndex = 0; argumentIndex < argumentCount; ++argumentIndex)
    {
        commandLine[offset++] = L' ';
        offset = AppendQuotedArgument(commandLine, offset, arguments[argumentIndex]);
    }

    commandLine[offset] = L'\0';
    return commandLine;
}

static LPWSTR BuildGuiCommandLine(
    HANDLE heap,
    LPCWSTR guiPath,
    int argumentCount,
    LPWSTR *arguments)
{
    LPCWSTR defaultArguments[1];
    if (argumentCount == 1)
    {
        defaultArguments[0] = DefaultArgument;
        return BuildCommandLine(heap, guiPath, 1, defaultArguments);
    }

    return BuildCommandLine(
        heap,
        guiPath,
        argumentCount - 1,
        (LPCWSTR *)(arguments + 1));
}

static void WriteFailure(const char *message, DWORD length)
{
    HANDLE errorHandle = GetStdHandle(STD_ERROR_HANDLE);
    DWORD ignored;
    if (errorHandle != NULL)
    {
        WriteFile(errorHandle, message, length, &ignored, NULL);
    }
}

static void HideNewConsoleForDesktopLaunch(void)
{
    DWORD processIds[2];
    if (GetConsoleProcessList(processIds, 2) == 1)
    {
        HWND consoleWindow = GetConsoleWindow();
        if (consoleWindow != NULL)
        {
            ShowWindow(consoleWindow, SW_HIDE);
        }
    }
}

static BOOL IsValidHandle(HANDLE handle)
{
    return handle != NULL && handle != INVALID_HANDLE_VALUE;
}

static int DuplicateStandardHandles(STARTUPINFOW *startupInfo)
{
    HANDLE currentProcess = GetCurrentProcess();
    HANDLE input = GetStdHandle(STD_INPUT_HANDLE);
    HANDLE output = GetStdHandle(STD_OUTPUT_HANDLE);
    HANDLE error = GetStdHandle(STD_ERROR_HANDLE);
    HANDLE nullInput = NULL;

    if (!IsValidHandle(output) && !IsValidHandle(error))
    {
        return STANDARD_HANDLES_UNAVAILABLE;
    }

    if (!IsValidHandle(output) || !IsValidHandle(error))
    {
        return STANDARD_HANDLES_ERROR;
    }

    if (!IsValidHandle(input))
    {
        nullInput = CreateFileW(
            NullDeviceName,
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            NULL,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL,
            NULL);
        if (!IsValidHandle(nullInput))
        {
            return STANDARD_HANDLES_ERROR;
        }

        input = nullInput;
    }

    if (!DuplicateHandle(
            currentProcess,
            input,
            currentProcess,
            &startupInfo->hStdInput,
            0,
            TRUE,
            DUPLICATE_SAME_ACCESS)
        || !DuplicateHandle(
            currentProcess,
            output,
            currentProcess,
            &startupInfo->hStdOutput,
            0,
            TRUE,
            DUPLICATE_SAME_ACCESS)
        || !DuplicateHandle(
            currentProcess,
            error,
            currentProcess,
            &startupInfo->hStdError,
            0,
            TRUE,
            DUPLICATE_SAME_ACCESS))
    {
        if (IsValidHandle(nullInput))
        {
            CloseHandle(nullInput);
        }

        if (startupInfo->hStdInput != NULL)
        {
            CloseHandle(startupInfo->hStdInput);
            startupInfo->hStdInput = NULL;
        }

        if (startupInfo->hStdOutput != NULL)
        {
            CloseHandle(startupInfo->hStdOutput);
            startupInfo->hStdOutput = NULL;
        }

        if (startupInfo->hStdError != NULL)
        {
            CloseHandle(startupInfo->hStdError);
            startupInfo->hStdError = NULL;
        }

        return STANDARD_HANDLES_ERROR;
    }

    if (IsValidHandle(nullInput))
    {
        CloseHandle(nullInput);
    }

    startupInfo->dwFlags |= STARTF_USESTDHANDLES;
    return STANDARD_HANDLES_READY;
}

static void CloseDuplicatedStandardHandles(STARTUPINFOW *startupInfo)
{
    if ((startupInfo->dwFlags & STARTF_USESTDHANDLES) != 0)
    {
        CloseHandle(startupInfo->hStdInput);
        CloseHandle(startupInfo->hStdOutput);
        CloseHandle(startupInfo->hStdError);
    }
}

static BOOL RunChild(
    LPCWSTR executablePath,
    LPWSTR commandLine,
    BOOL asynchronous,
    DWORD *exitCode)
{
    STARTUPINFOW startupInfo;
    PROCESS_INFORMATION processInformation;
    int standardHandleStatus;

    if (asynchronous)
    {
        HideNewConsoleForDesktopLaunch();
    }

    ZeroBytes(&startupInfo, sizeof(startupInfo));
    ZeroBytes(&processInformation, sizeof(processInformation));
    startupInfo.cb = (DWORD)sizeof(startupInfo);
    standardHandleStatus = DuplicateStandardHandles(&startupInfo);
    if (standardHandleStatus == STANDARD_HANDLES_ERROR)
    {
        return FALSE;
    }

    if (!CreateProcessW(
        executablePath,
        commandLine,
        NULL,
        NULL,
        TRUE,
        0,
        NULL,
        NULL,
        &startupInfo,
        &processInformation))
    {
        CloseDuplicatedStandardHandles(&startupInfo);
        return FALSE;
    }

    CloseDuplicatedStandardHandles(&startupInfo);
    CloseHandle(processInformation.hThread);
    if (asynchronous)
    {
        CloseHandle(processInformation.hProcess);
        *exitCode = 0;
        return TRUE;
    }

    if (WaitForSingleObject(processInformation.hProcess, INFINITE) == WAIT_OBJECT_0
        && GetExitCodeProcess(processInformation.hProcess, exitCode))
    {
        CloseHandle(processInformation.hProcess);
        return TRUE;
    }

    CloseHandle(processInformation.hProcess);
    return FALSE;
}

static BOOL ReadProductVersion(
    HANDLE heap,
    LPCWSTR executablePath,
    PRODUCT_VERSION *version)
{
    DWORD ignored = 0;
    DWORD dataLength = GetFileVersionInfoSizeW(executablePath, &ignored);
    void *data;
    void *value = NULL;
    unsigned int valueLength = 0;
    VS_FIXEDFILEINFO *fixedInfo;
    DWORD revision;
    BOOL result = FALSE;

    if (dataLength == 0)
    {
        return FALSE;
    }

    data = HeapAlloc(heap, 0, dataLength);
    if (data == NULL)
    {
        return FALSE;
    }

    if (GetFileVersionInfoW(executablePath, 0, dataLength, data)
        && VerQueryValueW(data, VersionRootQuery, &value, &valueLength)
        && value != NULL
        && valueLength >= sizeof(VS_FIXEDFILEINFO))
    {
        fixedInfo = (VS_FIXEDFILEINFO *)value;
        revision = fixedInfo->dwProductVersionLS & 0xffffUL;
        if (fixedInfo->dwSignature == VS_FIXEDFILEINFO_SIGNATURE && revision == 0)
        {
            version->major = (fixedInfo->dwProductVersionMS >> 16) & 0xffffUL;
            version->minor = fixedInfo->dwProductVersionMS & 0xffffUL;
            version->patch = (fixedInfo->dwProductVersionLS >> 16) & 0xffffUL;
            result = TRUE;
        }
    }

    HeapFree(heap, 0, data);
    return result;
}

static BOOL VersionsEqual(const PRODUCT_VERSION *left, const PRODUCT_VERSION *right)
{
    return left->major == right->major
        && left->minor == right->minor
        && left->patch == right->patch;
}

static SIZE_T DecimalLength(DWORD value)
{
    SIZE_T length = 1;
    while (value >= 10)
    {
        value /= 10;
        ++length;
    }

    return length;
}

static SIZE_T AppendDecimal(LPWSTR destination, SIZE_T offset, DWORD value)
{
    SIZE_T length = DecimalLength(value);
    SIZE_T index = length;
    while (index > 0)
    {
        destination[offset + index - 1] = (WCHAR)(L'0' + (value % 10));
        value /= 10;
        --index;
    }

    return offset + length;
}

static LPWSTR FormatVersionDirectory(HANDLE heap, const PRODUCT_VERSION *version)
{
    SIZE_T capacity = DecimalLength(version->major)
        + DecimalLength(version->minor)
        + DecimalLength(version->patch)
        + 4;
    SIZE_T offset = 0;
    LPWSTR value = (LPWSTR)HeapAlloc(heap, 0, capacity * sizeof(WCHAR));
    if (value == NULL)
    {
        return NULL;
    }

    value[offset++] = L'v';
    offset = AppendDecimal(value, offset, version->major);
    value[offset++] = L'.';
    offset = AppendDecimal(value, offset, version->minor);
    value[offset++] = L'.';
    offset = AppendDecimal(value, offset, version->patch);
    value[offset] = L'\0';
    return value;
}

static LPWSTR FormatCacheFileName(HANDLE heap, DWORD processId)
{
    SIZE_T prefixLength = StringLength(UpdateCacheFilePrefix);
    SIZE_T suffixLength = StringLength(ExecutableSuffix);
    SIZE_T capacity = prefixLength + DecimalLength(processId) + suffixLength + 1;
    SIZE_T offset = 0;
    SIZE_T index;
    LPWSTR value = (LPWSTR)HeapAlloc(heap, 0, capacity * sizeof(WCHAR));
    if (value == NULL)
    {
        return NULL;
    }

    for (index = 0; index < prefixLength; ++index)
    {
        value[offset++] = UpdateCacheFilePrefix[index];
    }

    offset = AppendDecimal(value, offset, processId);
    for (index = 0; index < suffixLength; ++index)
    {
        value[offset++] = ExecutableSuffix[index];
    }

    value[offset] = L'\0';
    return value;
}

static BOOL CreateDirectoryIfMissing(LPCWSTR path)
{
    if (CreateDirectoryW(path, NULL))
    {
        return TRUE;
    }

    return GetLastError() == ERROR_ALREADY_EXISTS;
}

static LPWSTR BuildCachedHostPath(
    HANDLE heap,
    const PRODUCT_VERSION *version)
{
    LPWSTR localAppData = NULL;
#ifdef USAGE_INDICATOR_E2E_TEST
    DWORD integrationLocalAppDataLength;
#endif
    LPWSTR productDirectory = NULL;
    LPWSTR cacheDirectory = NULL;
    LPWSTR versionName = NULL;
    LPWSTR versionDirectory = NULL;
    LPWSTR fileName = NULL;
    LPWSTR cachedPath = NULL;

#ifdef USAGE_INDICATOR_E2E_TEST
    localAppData = (LPWSTR)HeapAlloc(
        heap,
        0,
        INTEGRATION_LOCAL_APP_DATA_CAPACITY * sizeof(WCHAR));
    if (localAppData == NULL)
    {
        return NULL;
    }
    integrationLocalAppDataLength = GetEnvironmentVariableW(
        IntegrationLocalAppDataVariable,
        localAppData,
        INTEGRATION_LOCAL_APP_DATA_CAPACITY);
    if (integrationLocalAppDataLength == 0
        || integrationLocalAppDataLength >= INTEGRATION_LOCAL_APP_DATA_CAPACITY)
    {
        HeapFree(heap, 0, localAppData);
        return NULL;
    }
#else
    if (SHGetKnownFolderPath(&FolderIdLocalAppData, 0, NULL, &localAppData) != 0
        || localAppData == NULL)
    {
        return NULL;
    }
#endif

    productDirectory = JoinPath(heap, localAppData, UpdateCacheProductDirectory);
    cacheDirectory = productDirectory == NULL
        ? NULL
        : JoinPath(heap, productDirectory, UpdateCacheDirectory);
    versionName = FormatVersionDirectory(heap, version);
    versionDirectory = cacheDirectory == NULL || versionName == NULL
        ? NULL
        : JoinPath(heap, cacheDirectory, versionName);
    fileName = FormatCacheFileName(heap, GetCurrentProcessId());
    if (productDirectory != NULL
        && cacheDirectory != NULL
        && versionDirectory != NULL
        && fileName != NULL
        && CreateDirectoryIfMissing(productDirectory)
        && CreateDirectoryIfMissing(cacheDirectory)
        && CreateDirectoryIfMissing(versionDirectory))
    {
        cachedPath = JoinPath(heap, versionDirectory, fileName);
    }

    if (fileName != NULL)
    {
        HeapFree(heap, 0, fileName);
    }
    if (versionDirectory != NULL)
    {
        HeapFree(heap, 0, versionDirectory);
    }
    if (versionName != NULL)
    {
        HeapFree(heap, 0, versionName);
    }
    if (cacheDirectory != NULL)
    {
        HeapFree(heap, 0, cacheDirectory);
    }
    if (productDirectory != NULL)
    {
        HeapFree(heap, 0, productDirectory);
    }
#ifdef USAGE_INDICATOR_E2E_TEST
    HeapFree(heap, 0, localAppData);
#else
    CoTaskMemFree(localAppData);
#endif
    return cachedPath;
}

static BOOL IsPathInsideDirectory(LPCWSTR path, LPCWSTR directory)
{
    SIZE_T pathLength = StringLength(path);
    SIZE_T directoryLength = StringLength(directory);
    if (pathLength <= directoryLength
        || directoryLength > 0x7fffffffUL
        || CompareStringOrdinal(
            path,
            (int)directoryLength,
            directory,
            (int)directoryLength,
            TRUE) != CSTR_EQUAL)
    {
        return FALSE;
    }

    return path[directoryLength] == L'\\' || path[directoryLength] == L'/';
}

static DWORD RunUpdateHost(
    HANDLE heap,
    LPCWSTR installRoot,
    LPCWSTR command,
    BOOL *started)
{
    LPWSTR sourcePath = JoinPath(heap, installRoot, UpdateHostRelativePath);
    PRODUCT_VERSION sourceVersion;
    PRODUCT_VERSION cachedVersion;
    LPWSTR cachedPath = NULL;
    LPCWSTR hostArguments[6];
    LPWSTR commandLine = NULL;
    DWORD exitCode = 1;

    *started = FALSE;
    if (sourcePath == NULL || !ReadProductVersion(heap, sourcePath, &sourceVersion))
    {
        goto Cleanup;
    }

    cachedPath = BuildCachedHostPath(heap, &sourceVersion);
    if (cachedPath == NULL
        || IsPathInsideDirectory(cachedPath, installRoot)
        || !CopyFileW(sourcePath, cachedPath, FALSE)
        || !ReadProductVersion(heap, cachedPath, &cachedVersion)
        || !VersionsEqual(&sourceVersion, &cachedVersion))
    {
        goto Cleanup;
    }

    hostArguments[0] = CommandOption;
    hostArguments[1] = command;
    hostArguments[2] = InstallRootOption;
    hostArguments[3] = installRoot;
    hostArguments[4] = BootstrapVersionOption;
    hostArguments[5] = BootstrapVersion;
    commandLine = BuildCommandLine(heap, cachedPath, 6, hostArguments);
    if (commandLine == NULL)
    {
        exitCode = 1;
    }
    else
    {
        *started = RunChild(cachedPath, commandLine, FALSE, &exitCode);
        if (!*started)
        {
            exitCode = 1;
        }
    }

Cleanup:
    if (commandLine != NULL)
    {
        HeapFree(heap, 0, commandLine);
    }
    if (cachedPath != NULL)
    {
        HeapFree(heap, 0, cachedPath);
    }
    if (sourcePath != NULL)
    {
        HeapFree(heap, 0, sourcePath);
    }

    return exitCode;
}

void WINAPI LauncherEntry(void)
{
    HANDLE heap = GetProcessHeap();
    LPWSTR *arguments;
    int argumentCount = 0;
    LPWSTR launcherPath = NULL;
    LPWSTR binDirectory = NULL;
    LPWSTR installRoot = NULL;
    LPWSTR guiPath = NULL;
    LPWSTR commandLine = NULL;
    BOOL asynchronous;
    BOOL isUpdateCommand;
    BOOL updateHostStarted = FALSE;
    DWORD exitCode = 1;

    if (heap == NULL)
    {
        WriteFailure(
            GuiLaunchFailureMessage,
            (DWORD)(sizeof(GuiLaunchFailureMessage) - 1));
        ExitProcess(1);
    }

    arguments = CommandLineToArgvW(GetCommandLineW(), &argumentCount);
    if (arguments == NULL || argumentCount < 1)
    {
        WriteFailure(
            GuiLaunchFailureMessage,
            (DWORD)(sizeof(GuiLaunchFailureMessage) - 1));
        ExitProcess(1);
    }

    launcherPath = GetLauncherPath(heap);
    binDirectory = launcherPath == NULL
        ? NULL
        : GetParentDirectory(heap, launcherPath);
    installRoot = binDirectory == NULL
        ? NULL
        : GetParentDirectory(heap, binDirectory);
    isUpdateCommand = argumentCount == 2
        && (StringsEqual(arguments[1], CheckUpdateArgument)
            || StringsEqual(arguments[1], UpdateArgument));

    if (installRoot == NULL)
    {
        WriteFailure(
            isUpdateCommand ? UpdateHostLaunchFailureMessage : GuiLaunchFailureMessage,
            isUpdateCommand
                ? (DWORD)(sizeof(UpdateHostLaunchFailureMessage) - 1)
                : (DWORD)(sizeof(GuiLaunchFailureMessage) - 1));
        goto Cleanup;
    }

    if (isUpdateCommand)
    {
        exitCode = RunUpdateHost(heap, installRoot, arguments[1], &updateHostStarted);
        if (!updateHostStarted)
        {
            WriteFailure(
                UpdateHostLaunchFailureMessage,
                (DWORD)(sizeof(UpdateHostLaunchFailureMessage) - 1));
        }
        goto Cleanup;
    }

    guiPath = JoinPath(heap, installRoot, GuiRelativePath);
    commandLine = guiPath == NULL
        ? NULL
        : BuildGuiCommandLine(heap, guiPath, argumentCount, arguments);
    asynchronous = argumentCount == 2 && StringsEqual(arguments[1], AsyncArgument);
    if (guiPath == NULL
        || commandLine == NULL
        || !RunChild(guiPath, commandLine, asynchronous, &exitCode))
    {
        exitCode = 1;
        WriteFailure(
            GuiLaunchFailureMessage,
            (DWORD)(sizeof(GuiLaunchFailureMessage) - 1));
    }

Cleanup:
    if (commandLine != NULL)
    {
        HeapFree(heap, 0, commandLine);
    }
    if (guiPath != NULL)
    {
        HeapFree(heap, 0, guiPath);
    }
    if (installRoot != NULL)
    {
        HeapFree(heap, 0, installRoot);
    }
    if (binDirectory != NULL)
    {
        HeapFree(heap, 0, binDirectory);
    }
    if (launcherPath != NULL)
    {
        HeapFree(heap, 0, launcherPath);
    }
    LocalFree(arguments);
    ExitProcess(exitCode);
}
