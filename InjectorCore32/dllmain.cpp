#include <Windows.h>
#include <string>

extern "C" __declspec(dllexport)
BOOL __stdcall InjectDLL(
    DWORD pid,
    const wchar_t* dllPath,
    wchar_t* errorMsg,
    int errorMsgSize
) {
    HANDLE hProcess = OpenProcess(
        PROCESS_CREATE_THREAD |
        PROCESS_QUERY_INFORMATION |
        PROCESS_VM_OPERATION |
        PROCESS_VM_WRITE |
        PROCESS_VM_READ,
        FALSE,
        pid
    );

    if (!hProcess) {
        swprintf_s(errorMsg, errorMsgSize, L"OpenProcess failed");
        return FALSE;
    }

    SIZE_T pathLen = (wcslen(dllPath) + 1) * sizeof(wchar_t);

    LPVOID remoteMem = VirtualAllocEx(
        hProcess,
        nullptr,
        pathLen,
        MEM_COMMIT | MEM_RESERVE,
        PAGE_READWRITE
    );

    if (!remoteMem) {
        swprintf_s(errorMsg, errorMsgSize, L"VirtualAllocEx failed");
        CloseHandle(hProcess);
        return FALSE;
    }

    if (!WriteProcessMemory(
        hProcess,
        remoteMem,
        dllPath,
        pathLen,
        nullptr
    )) {
        swprintf_s(errorMsg, errorMsgSize, L"WriteProcessMemory failed");
        VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
        CloseHandle(hProcess);
        return FALSE;
    }

    HMODULE hKernel32 = GetModuleHandleW(L"kernel32.dll");
    FARPROC loadLibrary = GetProcAddress(hKernel32, "LoadLibraryW");

    if (!loadLibrary) {
        swprintf_s(errorMsg, errorMsgSize, L"LoadLibraryW not found");
        VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
        CloseHandle(hProcess);
        return FALSE;
    }

    HANDLE hThread = CreateRemoteThread(
        hProcess,
        nullptr,
        0,
        (LPTHREAD_START_ROUTINE)loadLibrary,
        remoteMem,
        0,
        nullptr
    );

    if (!hThread) {
        swprintf_s(errorMsg, errorMsgSize, L"CreateRemoteThread failed");
        VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
        CloseHandle(hProcess);
        return FALSE;
    }

    CloseHandle(hThread);
    CloseHandle(hProcess);
    return TRUE;
}
