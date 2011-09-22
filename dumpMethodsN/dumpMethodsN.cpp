/*
    Copyright (C) 2011 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

#include <Windows.h>
#include <tlhelp32.h>
#include <metahost.h>
#include <stdio.h>
#include <tchar.h>
#include <vector>
#include "DotNetFile.h"
#include "MakeWritable.h"
#include "DllAlloc.h"
#include "strex.h"
#include "log.h"
#include "common.h"

#pragma pack(push)
#pragma pack(1)
struct CORINFO_METHOD_INFO {
	void* ftn;
	void* scope;
	unsigned char* ILCode;
	unsigned ILCodeSize;
	unsigned short maxStack;
	unsigned short EHcount;
	char other[0x64];
};
#pragma pack(pop)

struct ScopeInfo {
	void* imageBase;
	CORINFO_METHOD_INFO cmi;
};

static const unsigned int DUMPED_METHODS_HEADER_MAGIC = 0x12345678;

#pragma pack(push)
#pragma pack(1)
struct DumpedMethodsHeader {
	unsigned int magic;
	unsigned int numMethods;
};

struct DumpedMethod {
	// method header fields
	unsigned short mhFlags;			// method header Flags
	unsigned short mhMaxStack;		// method header MaxStack
	unsigned int mhCodeSize;		// method header CodeSize
	unsigned int mhLocalVarSigTok;	// method header LocalVarSigTok

	// methodDef fields
	unsigned short mdImplFlags;		// methodDef ImplFlags
	unsigned short mdFlags;			// methodDef Flags
	unsigned int mdName;			// methodDef Name (index into #String)
	unsigned int mdSignature;		// methodDef Signature (index into #Blob)
	unsigned int mdParamList;		// methodDef ParamList (index into Param table)

	// Misc
	unsigned int token;				// metadata token
};
#pragma pack(pop)

void hookCompileMethod();
const ScopeInfo* findDotNetAssembly(const DotNetFile* dotNetFile);

DotNetFile* dotNetFile = 0;
DllAlloc* dllAlloc = 0;
HMODULE hJitDll = 0;			// eg. HMODULE of "clrjit.dll"
void* dotNetAssemblyBase = 0;	// Image base of .NET assembly we should dump
CORINFO_METHOD_INFO defaultCmi;
std::wstring inputFilename;		// Path of input file

typedef void* (__stdcall *getJit_t)(void);
typedef int (__stdcall* compileMethod_t)(void* self, void* comp,
	CORINFO_METHOD_INFO* info, unsigned flags, BYTE** nativeEntry,
	ULONG* nativeSizeOfCode);
compileMethod_t original_compileMethod = 0;
compileMethod_t ourObfuscatedCompileMethod = 0;
getJit_t getJit_func = 0;

struct MyCorinfoMethodInfo {
	static const int SIG = 0xFA149C31;

	MyCorinfoMethodInfo(const CORINFO_METHOD_INFO& _cmi)
		: cmi(_cmi), sig(SIG) {
	}

	CORINFO_METHOD_INFO cmi;	// Must be first field
	void* data;
	int sig;	// Always SIG so we know when we're dumping methods
};

struct DumpMethodContext {
	DumpMethodContext() : f(0), numMethods(0) {}
	~DumpMethodContext() {
		if (f)
			fclose(f);
	}
	FILE* f;
	unsigned numMethods;
	unsigned token;
	const char* name;
	size_t methodHeaderRva;
	unsigned char* methodHeader;
	size_t headerSize;
	void* methodBody;
	unsigned LocalVarSigTok;
	unsigned short flags;
	void* methodDef;
	const MetaDataType* type;
};

getJit_t getGetJitFunc() {
	getJit_t getJit_func = (getJit_t)GetProcAddress(hJitDll, "getJit");
	if (!getJit_func)
		throw strex("Could not get address of getJit() function");
	return getJit_func;
}

static const char* wtoa(const wchar_t* s) {
	static char buf[8*1024];
	int i = 0;
	do {
		if (i >= sizeof(buf))
			throw strex("Buffer overflow in wtoa");
		buf[i] = (char)s[i];
		i++;
	} while (s[i] != 0);
	return buf;
}

HMODULE getJitDllModule() {
#if 0
	ICLRMetaHost* mh;
	if (CLRCreateInstance(CLSID_CLRMetaHost, IID_ICLRMetaHost, (LPVOID*)&mh) != S_OK)
		throw strex("Could not get ref to CLRMetaHost");
	ICLRRuntimeInfo* rtti;
	// Version must match a version dir in %WINDIR%\Microsoft.NET\Framework
	if (mh->GetRuntime(L"v4.0.30319", IID_ICLRRuntimeInfo, (LPVOID*)&rtti) != S_OK)
		throw strex("Could not get ref to ICLRRuntimeInfo");
	HMODULE hClr;
	if (rtti->LoadLibrary(L"clrjit.dll", &hClr) != S_OK)
		throw strex("Could not load JIT dll file");
	return hClr;
#else
	HMODULE hClr = LoadLibraryW(L"clrjit.dll");
	if (hClr != 0) {
		logv("Loaded CLR JIT DLL %s\n", "clrjit.dll");
		return hClr;
	}
	wchar_t name[1024];
	DWORD size = sizeof(name);
	DWORD len;
	if (GetCORSystemDirectory(name, size, &len) != S_OK)
		throw strex("Could not get CLR installation directory");
	wcscat(name, L"mscorjit.dll");
	hClr = LoadLibraryW(name);
	if (hClr != 0) {
		logv("Loaded CLR JIT DLL %s\n", wtoa(name));
		return hClr;
	}
	throw strex("Could not load JIT DLL");
#endif
}

// Returns base addr or NULL
void* findModule(void* addr) {
	HANDLE hSnap;
	for (;;) {
		hSnap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE, 0);
		if (hSnap != INVALID_HANDLE_VALUE)
			break;
		if (GetLastError() != ERROR_BAD_LENGTH)
			break;
	}
	if (hSnap == INVALID_HANDLE_VALUE)
		throw strex("Could not get module snapshot");

	MODULEENTRY32 me32;
	me32.dwSize = sizeof(me32);
	void* baseAddr = 0;
	for (BOOL res = Module32First(hSnap, &me32); res; res = Module32Next(hSnap, &me32)) {
		if (me32.modBaseAddr <= addr && addr <= me32.modBaseAddr + me32.modBaseSize - 1) {
			baseAddr = me32.modBaseAddr;
		}
	}

	CloseHandle(hSnap);
	return baseAddr;
}

bool hasProtectionHookedJit() {
	compileMethod_t* jit_vtbl = *(compileMethod_t**)getJit_func();
	return *jit_vtbl != ourObfuscatedCompileMethod;
}

// Used only by the following two methods
static const char* dump_methodName = 0;
static unsigned dump_token = 0;
const char* __stdcall returnNameOfMethod(void* arg1, void* arg2, void* arg3) {
	(void)arg1; (void)arg2; (void)arg3;
	return dump_methodName;
}

unsigned __stdcall returnMethodToken(void* arg1, void* arg2) {
	(void)arg1; (void)arg2;
	return dump_token;
}

bool dumpMethodIndex(unsigned index, DumpMethodContext* ctx) {
	dump_token = 0x06000000 + index;
	dump_methodName = dotNetFile->getMethodName(index, dotNetAssemblyBase);
	logvv("\n========================================================\n");
	logvv("Method name: '%s' (%08X), token: %08X\n", dump_methodName, dump_methodName, dump_token);

	unsigned char* methodHeader = (unsigned char*)dotNetFile->getMethodHeader(index, dotNetAssemblyBase);
	if (!methodHeader)
		throw strex("Could not get method header");
	if (methodHeader != dotNetAssemblyBase)
		dumpMem(methodHeader, 16, "methodHeader");

	size_t headerSize, codeSize, LocalVarSigTok, methodHeaderRva;
	unsigned short maxStack, flags;
	void* methodBody;
	if (methodHeader == dotNetAssemblyBase) {
		headerSize = 0;
		maxStack = 0;
		codeSize = 0;
		flags = 0;
		LocalVarSigTok = 0;
		methodBody = 0;
		methodHeaderRva = 0;
	}
	else if ((*methodHeader & 3) == 2) {	// Tiny header
		headerSize = 1;
		maxStack = 8;
		codeSize = *methodHeader >> 2;
		flags = 2;
		LocalVarSigTok = 0;
		methodBody = methodHeader + headerSize;
		methodHeaderRva = methodHeader - (unsigned char*)dotNetAssemblyBase;
	}
	else {	// Fat header
		headerSize = (methodHeader[1] >> 4) * 4;
		maxStack = *(unsigned short*)(methodHeader + 2);
		codeSize = *(unsigned int*)(methodHeader + 4);
		flags = *(WORD*)methodHeader;
		LocalVarSigTok = *(DWORD*)(methodHeader + 8);
		methodBody = methodHeader + headerSize;
		methodHeaderRva = methodHeader - (unsigned char*)dotNetAssemblyBase;
	}

	ctx->token = dump_token;
	ctx->name = dump_methodName;
	ctx->methodHeaderRva = methodHeaderRva;
	ctx->methodHeader = methodHeader;
	ctx->headerSize = headerSize;
	ctx->methodBody = methodBody;
	ctx->flags = flags;
	ctx->LocalVarSigTok = LocalVarSigTok;
	ctx->methodDef = dotNetFile->getMethodDef(index, dotNetAssemblyBase);
	ctx->type = dotNetFile->getMethodType();

	MyCorinfoMethodInfo info = defaultCmi;
	info.data = ctx;
	info.cmi.ILCode = (BYTE*)methodBody;
	info.cmi.ILCodeSize = codeSize;
	info.cmi.maxStack = maxStack;

	void* mem[0x10];
	memset(mem, 0, sizeof(mem));
	mem[1] = &mem[2];
	mem[3] = (void*)0x14;
	mem[5] = (void*)0x1C;
	mem[6] = &mem[7];
	mem[7] = returnNameOfMethod;
	mem[8] = &mem[0];
	mem[13] = returnMethodToken;	// Older .NET version
	mem[14] = returnMethodToken;	// Newer .NET version

	void* self = getJit_func();
	BYTE* nativeEntry = 0;
	ULONG nativeSizeOfCode = 0;
	void* comp = mem;
	compileMethod_t compileMethod = **(compileMethod_t**)self;
	logvv("Calling protection to decrypt code, scope: %08X, token: %08X\n", info.cmi.scope, dump_token);
	compileMethod(self, comp, &info.cmi, 0, &nativeEntry, &nativeSizeOfCode);
	logvv("Calling protection to decrypt code: DONE\n");

	return true;
}

extern "C" __declspec(dllexport) int __stdcall dumpCode(const wchar_t* methodsFilename) {
	try {
		if (!hasProtectionHookedJit()) {
			// CliSecure sometimes fails to load for an unknown reason. It happens rarely, but
			// it doesn't seem to happen when I use .NET 2.0, only 4.0.
			loge("Protection hasn't hooked JIT yet! Try again or try .NET != 4.0!\n");
			return 0;
		}

		std::wstring name(inputFilename + L".methods");
		if (methodsFilename != 0 && *methodsFilename != 0)
			name = methodsFilename;

		DumpMethodContext ctx;
		ctx.f = _wfopen(name.c_str(), L"wb");
		if (!ctx.f)
			throw strex("Could not create methods file");

		DumpedMethodsHeader header = {0};
		header.magic = DUMPED_METHODS_HEADER_MAGIC;

		if (fwrite(&header, 1, sizeof(header), ctx.f) != sizeof(header))
			throw strex("Could not write to dumped methods file");

		const size_t numMethods = dotNetFile->getNumMethods(); (void)numMethods;
		for (size_t i = 1; i <= numMethods; i++)
			dumpMethodIndex(i, &ctx);

		if (fseek(ctx.f, 0, SEEK_SET))
			throw strex("Could not seek #3");
		header.numMethods = ctx.numMethods;
		if (fwrite(&header, 1, sizeof(header), ctx.f) != sizeof(header))
			throw strex("Could not write to dumped methods file");
	}
	catch (std::exception& ex) {
		loge("EXCEPTION #3: %s\n", ex.what());
		return 0;
	}

	return 1;
}

#define PE_ALIGNMENT 0x10000

bool isPeImage(void* addr) {
	__try {
		if (!addr)
			return false;
		if (!IS_ALIGNED((size_t)addr, PE_ALIGNMENT))
			return false;

		unsigned char* p = (unsigned char*)addr;
		IMAGE_DOS_HEADER* idh = (IMAGE_DOS_HEADER*)p;
		if (idh->e_magic != IMAGE_DOS_SIGNATURE)
			return false;
		p += idh->e_lfanew;
		if (*(DWORD*)p != IMAGE_NT_SIGNATURE)
			return false;
	}
	__except (EXCEPTION_EXECUTE_HANDLER) {
		return false;
	}

	return true;
}

static void* __getImagebase1(void* addr) {
	HMODULE hMod;
	if (GetModuleHandleEx(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS, (LPCSTR)addr, &hMod) == 0)
		return 0;
	return (void*)hMod;
}

static void* __getImagebase2(void* addr) {
	MEMORY_BASIC_INFORMATION mbi;
	if (VirtualQuery(addr, &mbi, sizeof(mbi)) == 0)
		return 0;
	return mbi.AllocationBase;
}

static void* __getImagebase3(void* addr) {
	size_t offs = ALIGN_DOWN((size_t)addr, PE_ALIGNMENT);
	for ( ; offs; offs -= PE_ALIGNMENT) {
		if (IsBadReadPtr((const void*)offs, 1))
			return 0;	// A PE image shouldn't have empty holes >= PE_ALIGNMENT
		if (isPeImage((void*)offs))
			return (void*)offs;
	}

	return 0;
}

void* filterImageBase(void* addr) {
	if (isPeImage(addr))
		return addr;
	return 0;
}

void* findImageBase(void* addr) {
	void* imageBase = 0;
	if (imageBase == 0)
		imageBase = filterImageBase(findModule(addr));
	if (imageBase == 0)
		imageBase = filterImageBase(__getImagebase1(addr));
	if (imageBase == 0)
		imageBase = filterImageBase(__getImagebase2(addr));
	if (imageBase == 0)
		imageBase = filterImageBase(__getImagebase3(addr));
	return imageBase;
}

std::vector<ScopeInfo> imageBases;

void addImageBase(void* imageBase, const CORINFO_METHOD_INFO* info) {
	if (!imageBase)
		return;

	for (size_t i = 0; i < imageBases.size(); i++) {
		if (imageBases[i].imageBase == imageBase) {
			if (imageBases[i].cmi.scope != info->scope)
				logw("===WARNING=== new scope but same imageBase! %08X vs %08X\n", imageBases[i].cmi.scope, info->scope);
			return;
		}
	}

	logv("%d. Added imageBase %08X\n", imageBases.size() + 1, imageBase);
	ScopeInfo si;
	si.cmi = *info;
	si.imageBase = imageBase;
	imageBases.push_back(si);
}

const ScopeInfo* findDotNetAssembly(const DotNetFile* dotNetFile) {
	for (size_t i = 0; i < imageBases.size(); i++) {
		if (dotNetFile->isYourImageBase(imageBases[i].imageBase)) {
			return &imageBases[i];
		}
	}

	return 0;
}

int __stdcall myCompileMethod(void* self, void* comp,
	CORINFO_METHOD_INFO* info, unsigned flags, BYTE** nativeEntry,
	ULONG* nativeSizeOfCode) {

	if (((MyCorinfoMethodInfo*)info)->sig == MyCorinfoMethodInfo::SIG) {
		// We're dumping methods

		logvv("code: %08X, size: %08X, maxStack: %04X\n", info->ILCode, info->ILCodeSize, info->maxStack);

		DumpMethodContext* ctx = (DumpMethodContext*)((MyCorinfoMethodInfo*)info)->data;
		dumpMem(info->ILCode, info->ILCodeSize, "ILCode");
		dumpMem(ctx->methodDef, ctx->type->size(), "methodDef table");
		if (ctx->methodHeader)
			dumpMem(ctx->methodHeader, 16, "method header");

		DumpedMethod dm;

		dm.mhFlags = ctx->flags;
		dm.mhMaxStack = info->maxStack;
		dm.mhCodeSize = info->ILCodeSize;
		dm.mhLocalVarSigTok = ctx->LocalVarSigTok;

		dm.mdImplFlags = (unsigned short)ctx->type->read(ctx->methodDef, 1);
		dm.mdFlags = (unsigned short)ctx->type->read(ctx->methodDef, 2);
		dm.mdName = ctx->type->read(ctx->methodDef, 3);
		dm.mdSignature = ctx->type->read(ctx->methodDef, 4);
		dm.mdParamList = ctx->type->read(ctx->methodDef, 5);

		dm.token = ctx->token;

		dumpMem(&dm, sizeof(dm), "DumpedMethod");

		if (fwrite(&dm, 1, sizeof(dm), ctx->f) != sizeof(dm))
			throw strex("Could not write DumpedMethod");
		if (fwrite(info->ILCode, 1, info->ILCodeSize, ctx->f) != info->ILCodeSize)
			throw strex("Could not write ILCode");

		ctx->numMethods++;

		return 0;
	}
	else {
		void* imageBase = findImageBase(info->ILCode);
		addImageBase(imageBase, info);
		logvv("myCompileMethod: called. %08X (%3d), base: %08X, info: %08X, scope: %08X\n", info->ILCode, info->ILCodeSize, imageBase, info, info->scope);

		return original_compileMethod(self, comp, info, flags, nativeEntry, nativeSizeOfCode);
	}
}

// dest => memory to write to (start of method). Must be HOOK_METHOD_SIZE bytes.
// destFunc => the real func we should execute
void createHookMethod(void* dest, void* destFunc) {
	/*
	 * Obfuscate the code a little bit just in case a protection checks
	 * for JMP XYZ.
	 * B8 xx xx xx xx	mov eax, xxxxxxxx
	 * 2D xx xx xx xx	sub eax, xxxxxxxx
	 * 50				push eax
	 * C3				retn
	 */
#define HOOK_METHOD_SIZE 12

	const size_t offs = (size_t)destFunc;
	unsigned char* p = (unsigned char*)dest;
	*p++ = 0xB8;
	*(DWORD*)p = offs + 0xC0DEBEEF;
	p += 4;
	*p++ = 0x2D;
	*(DWORD*)p = 0xC0DEBEEF;
	p += 4;
	*p++ = 0x50;
	*p++ = 0xC3;
}

void hookCompileMethod() {
	ourObfuscatedCompileMethod = (compileMethod_t)dllAlloc->alloc(HOOK_METHOD_SIZE);
	{
		MakeWritable dummy(ourObfuscatedCompileMethod, HOOK_METHOD_SIZE);
		createHookMethod(ourObfuscatedCompileMethod, myCompileMethod);
	}

	getJit_func = getGetJitFunc();
	compileMethod_t* jit_vtbl = *(compileMethod_t**)getJit_func();
	original_compileMethod = *jit_vtbl;
	{
		MakeWritable dummy(jit_vtbl, sizeof(void*));
		*jit_vtbl = ourObfuscatedCompileMethod;
	}
	logv("Original compileMethod: %p\n", original_compileMethod);
}

extern "C" __declspec(dllexport) int __stdcall initialize1(int logLevel, const wchar_t* filename) {
	static bool initialized = false;
	int ret = 1;
	if (!initialized) {
		try {
			setLogLevel(logLevel);

			hJitDll = getJitDllModule();

			dllAlloc = new DllAlloc(hJitDll);
			hookCompileMethod();

			inputFilename = filename;
			dotNetFile = new DotNetFile(filename);

			initialized = true;
		}
		catch (std::exception& ex) {
			loge("EXCEPTION #1: %s\n", ex.what());
			ret = 0;
		}
	}

	return ret;
}

extern "C" __declspec(dllexport) int __stdcall initialize2() {
	static bool initialized = false;
	int ret = 1;
	if (!initialized) {
		try {
			const ScopeInfo* si = findDotNetAssembly(dotNetFile);
			if (!si)
				throw strex("Could not find .NET assembly's image base");
			dotNetAssemblyBase = si->imageBase;
			defaultCmi = si->cmi;

			initialized = true;
		}
		catch (std::exception& ex) {
			loge("EXCEPTION #2: %s\n", ex.what());
			ret = 0;
		}
	}

	return ret;
}

extern "C" __declspec(dllexport) int __stdcall foundAssembly() {
	return findDotNetAssembly(dotNetFile) != 0;
}

extern "C" __declspec(dllexport) void __stdcall debug_addImageBase(void* imageBase) {
	CORINFO_METHOD_INFO info = {0};
	addImageBase(imageBase, &info);
}
