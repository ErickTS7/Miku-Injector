#include <windows.h>
#include <iostream>

bool InjectDLL(DWORD pid, const wchar_t* dllPath)
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
        return false;

    size_t pathLen = (wcslen(dllPath) + 1) * sizeof(wchar_t);

    LPVOID remoteMem = VirtualAllocEx(
        hProcess,
        nullptr,
        pathLen,
        MEM_COMMIT | MEM_RESERVE,
        PAGE_READWRITE
    );

    if (!remoteMem)
    {
        CloseHandle(hProcess);
        return false;
    }

    if (!WriteProcessMemory(
        hProcess,
        remoteMem,
        dllPath,
        pathLen,
        nullptr
    ))
    {
        VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
        CloseHandle(hProcess);
        return false;
    }

    HMODULE hKernel32 = GetModuleHandleW(L"kernel32.dll");
    if (!hKernel32)
    {
        VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
        CloseHandle(hProcess);
        return false;
    }

    LPVOID loadLibraryAddr = GetProcAddress(hKernel32, "LoadLibraryW");
    if (!loadLibraryAddr)
    {
        VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
        CloseHandle(hProcess);
        return false;
    }

    HANDLE hThread = CreateRemoteThread(
        hProcess,
        nullptr,
        0,
        (LPTHREAD_START_ROUTINE)loadLibraryAddr,
        remoteMem,
        0,
        nullptr
    );

    if (!hThread)
    {
        VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
        CloseHandle(hProcess);
        return false;
    }

    WaitForSingleObject(hThread, INFINITE);

    CloseHandle(hThread);
    VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
    CloseHandle(hProcess);

    return true;
}

int wmain(int argc, wchar_t* argv[])
{
    if (argc < 3)
        return 1;

    DWORD pid = wcstoul(argv[1], nullptr, 10);
    const wchar_t* dllPath = argv[2];

    if (pid == 0 || !dllPath)
        return 2;

    if (!InjectDLL(pid, dllPath))
        return 3;

    return 0;
}
