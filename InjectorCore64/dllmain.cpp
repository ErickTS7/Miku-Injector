#include <Windows.h>

extern "C" __declspec(dllexport)
BOOL InjectDLL(
    DWORD pid,
    const wchar_t* dllPath,
    wchar_t* errorMsg,
    int errorMsgSize
)
{
    HANDLE hProcess = OpenProcess(
        PROCESS_CREATE_THREAD |
        PROCESS_QUERY_INFORMATION |
        PROCESS_VM_OPERATION |
        PROCESS_VM_WRITE |
        PROCESS_VM_READ,
        FALSE,
        pid
    );

    if (!hProcess)
    {
        wsprintfW(errorMsg, L"OpenProcess failed (%lu)", GetLastError());
        return FALSE;
    }

    size_t dllPathSize = (wcslen(dllPath) + 1) * sizeof(wchar_t);

    LPVOID remoteMem = VirtualAllocEx(
        hProcess,
        nullptr,
        dllPathSize,
        MEM_COMMIT | MEM_RESERVE,
        PAGE_READWRITE
    );

    if (!remoteMem)
    {
        wsprintfW(errorMsg, L"VirtualAllocEx failed (%lu)", GetLastError());
        CloseHandle(hProcess);
        return FALSE;
    }

    if (!WriteProcessMemory(
        hProcess,
        remoteMem,
        dllPath,
        dllPathSize,
        nullptr
    ))
    {
        wsprintfW(errorMsg, L"WriteProcessMemory failed (%lu)", GetLastError());
        VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
        CloseHandle(hProcess);
        return FALSE;
    }

    HMODULE hKernel32 = GetModuleHandleW(L"kernel32.dll");
    if (!hKernel32)
    {
        wsprintfW(errorMsg, L"GetModuleHandle failed");
        VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
        CloseHandle(hProcess);
        return FALSE;
    }

    LPTHREAD_START_ROUTINE loadLibrary =
        (LPTHREAD_START_ROUTINE)GetProcAddress(hKernel32, "LoadLibraryW");

    if (!loadLibrary)
    {
        wsprintfW(errorMsg, L"GetProcAddress failed");
        VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
        CloseHandle(hProcess);
        return FALSE;
    }

    HANDLE hThread = CreateRemoteThread(
        hProcess,
        nullptr,
        0,
        loadLibrary,
        remoteMem,
        0,
        nullptr
    );

    if (!hThread)
    {
        wsprintfW(errorMsg, L"CreateRemoteThread failed (%lu)", GetLastError());
        VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
        CloseHandle(hProcess);
        return FALSE;
    }

    WaitForSingleObject(hThread, INFINITE);

    CloseHandle(hThread);
    CloseHandle(hProcess);

    return TRUE;
}
