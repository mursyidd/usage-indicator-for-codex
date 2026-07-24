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

typedef unsigned short WORD;
typedef unsigned long DWORD;
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

__declspec(dllimport) LPWSTR WINAPI GetCommandLineW(void);
__declspec(dllimport) LPWSTR *WINAPI CommandLineToArgvW(LPCWSTR commandLine, int *argumentCount);
__declspec(dllimport) HANDLE WINAPI LocalFree(HANDLE memory);
__declspec(dllimport) DWORD WINAPI GetModuleFileNameW(HANDLE module, LPWSTR fileName, DWORD size);
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

static const WCHAR GuiRelativePath[] = L"..\\app\\UsageIndicatorForCodex.Gui.exe";
static const WCHAR DefaultArgument[] = L"help";
static const WCHAR AsyncArgument[] = L"start";
static const WCHAR NullDeviceName[] = L"NUL";
static const char LaunchFailureMessage[] =
    "usage-indicator.exe could not start UsageIndicatorForCodex.Gui.exe.\r\n";

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

static LPWSTR GetGuiPath(HANDLE heap, LPCWSTR launcherPath)
{
    SIZE_T launcherLength = StringLength(launcherPath);
    SIZE_T nameLength = StringLength(GuiRelativePath);
    SIZE_T directoryLength = launcherLength;
    SIZE_T index;
    LPWSTR guiPath;

    while (directoryLength > 0
        && launcherPath[directoryLength - 1] != L'\\'
        && launcherPath[directoryLength - 1] != L'/')
    {
        --directoryLength;
    }

    guiPath = (LPWSTR)HeapAlloc(
        heap,
        0,
        (directoryLength + nameLength + 1) * sizeof(WCHAR));
    if (guiPath == NULL)
    {
        return NULL;
    }

    for (index = 0; index < directoryLength; ++index)
    {
        guiPath[index] = launcherPath[index];
    }

    for (index = 0; index < nameLength; ++index)
    {
        guiPath[directoryLength + index] = GuiRelativePath[index];
    }

    guiPath[directoryLength + nameLength] = L'\0';
    return guiPath;
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

static LPWSTR BuildChildCommandLine(
    HANDLE heap,
    LPCWSTR guiPath,
    int argumentCount,
    LPWSTR *arguments)
{
    SIZE_T characterCapacity = StringLength(guiPath) * 2 + 3;
    SIZE_T offset = 0;
    int argumentIndex;
    LPWSTR commandLine;

    for (argumentIndex = 1; argumentIndex < argumentCount; ++argumentIndex)
    {
        characterCapacity += StringLength(arguments[argumentIndex]) * 2 + 4;
    }

    if (argumentCount == 1 && DefaultArgument[0] != L'\0')
    {
        characterCapacity += StringLength(DefaultArgument) * 2 + 4;
    }

    commandLine = (LPWSTR)HeapAlloc(
        heap,
        HEAP_ZERO_MEMORY,
        characterCapacity * sizeof(WCHAR));
    if (commandLine == NULL)
    {
        return NULL;
    }

    offset = AppendQuotedArgument(commandLine, offset, guiPath);
    if (argumentCount == 1 && DefaultArgument[0] != L'\0')
    {
        commandLine[offset++] = L' ';
        offset = AppendQuotedArgument(commandLine, offset, DefaultArgument);
    }
    else for (argumentIndex = 1; argumentIndex < argumentCount; ++argumentIndex)
    {
        commandLine[offset++] = L' ';
        offset = AppendQuotedArgument(commandLine, offset, arguments[argumentIndex]);
    }

    commandLine[offset] = L'\0';
    return commandLine;
}

static void WriteLaunchFailure(void)
{
    HANDLE errorHandle = GetStdHandle(STD_ERROR_HANDLE);
    DWORD ignored;
    if (errorHandle != NULL)
    {
        WriteFile(
            errorHandle,
            LaunchFailureMessage,
            (DWORD)(sizeof(LaunchFailureMessage) - 1),
            &ignored,
            NULL);
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

void WINAPI LauncherEntry(void)
{
    HANDLE heap = GetProcessHeap();
    LPWSTR *arguments;
    int argumentCount = 0;
    LPWSTR launcherPath;
    LPWSTR guiPath;
    LPWSTR commandLine;
    STARTUPINFOW startupInfo;
    PROCESS_INFORMATION processInformation;
    BOOL asynchronous;
    int standardHandleStatus;
    DWORD exitCode = 1;

    if (heap == NULL)
    {
        WriteLaunchFailure();
        ExitProcess(1);
    }

    arguments = CommandLineToArgvW(GetCommandLineW(), &argumentCount);
    if (arguments == NULL || argumentCount < 1)
    {
        WriteLaunchFailure();
        ExitProcess(1);
    }

    launcherPath = GetLauncherPath(heap);
    guiPath = launcherPath == NULL ? NULL : GetGuiPath(heap, launcherPath);
    commandLine = guiPath == NULL
        ? NULL
        : BuildChildCommandLine(heap, guiPath, argumentCount, arguments);
    if (launcherPath == NULL || guiPath == NULL || commandLine == NULL)
    {
        WriteLaunchFailure();
        ExitProcess(1);
    }

    asynchronous = argumentCount == 2 && StringsEqual(arguments[1], AsyncArgument);
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
        WriteLaunchFailure();
        ExitProcess(1);
    }

    if (!CreateProcessW(
        guiPath,
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
        WriteLaunchFailure();
        ExitProcess(1);
    }

    CloseDuplicatedStandardHandles(&startupInfo);
    CloseHandle(processInformation.hThread);
    if (asynchronous)
    {
        CloseHandle(processInformation.hProcess);
        exitCode = 0;
    }
    else if (WaitForSingleObject(processInformation.hProcess, INFINITE) == WAIT_OBJECT_0
        && GetExitCodeProcess(processInformation.hProcess, &exitCode))
    {
        CloseHandle(processInformation.hProcess);
    }
    else
    {
        CloseHandle(processInformation.hProcess);
        exitCode = 1;
    }

    HeapFree(heap, 0, commandLine);
    HeapFree(heap, 0, guiPath);
    HeapFree(heap, 0, launcherPath);
    LocalFree(arguments);
    ExitProcess(exitCode);
}
