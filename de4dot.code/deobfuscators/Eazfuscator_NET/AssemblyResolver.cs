/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Eazfuscator_NET {
	class AssemblyResolver {
		ModuleDefMD module;
		DecrypterType decrypterType;
		TypeDef resolverType;
		MethodDef initMethod;
		MethodDef handlerMethod;
		MethodDef decryptMethod;
		TypeDef otherType;
		List<AssemblyInfo> assemblyInfos = new List<AssemblyInfo>();
		FrameworkType frameworkType;
		byte[] decryptKey;
		CodeCompilerMethodCallRestorer codeCompilerMethodCallRestorer;

		public class AssemblyInfo {
			public bool IsEncrypted { get; set; }
			public bool IsCompressed { get; set; }
			public string ResourceName { get; set; }
			public string Filename { get; set; }
			public string AssemblyFullName { get; set; }
			public string SimpleName { get; set; }
			public string Extension { get; set; }
			public EmbeddedResource Resource { get; set; }
			public byte[] Data { get; set; }

			public override string ToString() {
				return AssemblyFullName ?? Filename;
			}
		}

		public TypeDef Type {
			get { return resolverType; }
		}

		public TypeDef OtherType {
			get { return otherType; }
		}

		public MethodDef InitMethod {
			get { return initMethod; }
		}

		public IEnumerable<AssemblyInfo> AssemblyInfos {
			get { return assemblyInfos; }
		}

		public bool Detected {
			get { return resolverType != null; }
		}

		public AssemblyResolver(ModuleDefMD module, DecrypterType decrypterType) {
			this.module = module;
			this.frameworkType = DotNetUtils.getFrameworkType(module);
			this.decrypterType = decrypterType;
			this.codeCompilerMethodCallRestorer = new CodeCompilerMethodCallRestorer(module);
		}

		public void find() {
			checkCalledMethods(DotNetUtils.getModuleTypeCctor(module));
		}

		bool checkCalledMethods(MethodDef method) {
			if (method == null || method.Body == null)
				return false;

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;

				var calledMethod = instr.Operand as MethodDef;
				if (calledMethod == null || !calledMethod.IsStatic || calledMethod.Body == null)
					continue;
				if (!DotNetUtils.isMethod(calledMethod, "System.Void", "()"))
					continue;

				if (frameworkType == FrameworkType.Silverlight) {
					if (!checkInitMethodSilverlight(calledMethod))
						continue;
				}
				else {
					if (!checkInitMethod(calledMethod))
						continue;
				}

				decryptMethod = getDecryptMethod();
				updateDecrypterType();
				findCodeDomMethods();
				return true;
			}

			return false;
		}

		bool checkInitMethodSilverlight(MethodDef method) {
			var type = method.DeclaringType;
			if (type.NestedTypes.Count != 2)
				return false;

			var resolveHandler = getResolveMethodSilverlight(method);
			if (resolveHandler == null)
				return false;

			initMethod = method;
			resolverType = type;
			handlerMethod = resolveHandler;
			return true;
		}

		static MethodDef getResolveMethodSilverlight(MethodDef initMethod) {
			foreach (var instr in initMethod.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;
				var calledMethod = instr.Operand as MethodDef;
				if (calledMethod == null)
					continue;
				if (!DotNetUtils.isMethod(calledMethod, "System.Void", "()"))
					continue;
				if (!DeobUtils.hasInteger(calledMethod, ',') ||
					!DeobUtils.hasInteger(calledMethod, '|'))
					continue;

				return calledMethod;
			}

			return null;
		}

		bool checkInitMethod(MethodDef method) {
			var type = method.DeclaringType;
			if (type.NestedTypes.Count < 2 || type.NestedTypes.Count > 6)
				return false;
			if (DotNetUtils.getPInvokeMethod(type, "kernel32", "MoveFileEx") == null)
				return false;

			var resolveHandler = DeobUtils.getResolveMethod(method);
			if (resolveHandler == null)
				return false;
			if (!DeobUtils.hasInteger(resolveHandler, ',') ||
				!DeobUtils.hasInteger(resolveHandler, '|'))
				return false;

			initMethod = method;
			resolverType = type;
			handlerMethod = resolveHandler;
			return true;
		}

		MethodDef getDecryptMethod() {
			foreach (var method in resolverType.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.isMethod(method, "System.Byte[]", "(System.Byte[])"))
					continue;
				if (!DeobUtils.hasInteger(method, 32) ||
					!DeobUtils.hasInteger(method, 121))
					continue;

				return method;
			}

			throw new ApplicationException("Could not find decrypt method");
		}

		void updateDecrypterType() {
			var theDecrypterType = getDecrypterType(decryptMethod);
			if (theDecrypterType == null)
				return;
			decrypterType.Type = theDecrypterType;
			if (!decrypterType.initialize())
				throw new ApplicationException("Could not initialize decrypterType");
		}

		TypeDef getDecrypterType(MethodDef method) {
			if (method == null || method.Body == null)
				return null;

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;
				var calledMethod = instr.Operand as MethodDef;
				if (calledMethod == null || !calledMethod.IsStatic || calledMethod.DeclaringType == resolverType)
					continue;
				if (!DotNetUtils.isMethod(calledMethod, "System.Void", "(System.Byte[])"))
					continue;

				return calledMethod.DeclaringType;
			}

			return null;
		}

		public void initialize(ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			if (handlerMethod == null)
				return;

			findOtherType();

			simpleDeobfuscator.deobfuscate(handlerMethod);
			simpleDeobfuscator.decryptStrings(handlerMethod, deob);
			if (!createAssemblyInfos())
				throw new ApplicationException("Could not initialize assembly infos");

			simpleDeobfuscator.deobfuscate(decryptMethod);
			simpleDeobfuscator.decryptStrings(decryptMethod, deob);
			if (!createDecryptKey())
				throw new ApplicationException("Could not initialize decryption key");
		}

		void findOtherType() {
			foreach (var type in module.Types) {
				// This type is added by EF 3.1+. The last number seems to be an int32 hash of
				// the assembly name, but - replaced with _.
				if (!Regex.IsMatch(type.FullName, @"^pc1eOx2WJVV[_0-9]+$"))
					continue;

				otherType = type;
				break;
			}
		}

		bool createDecryptKey() {
			if (decryptMethod == null)
				return false;

			foreach (var s in DotNetUtils.getCodeStrings(decryptMethod)) {
				decryptKey = decodeBase64(s);
				if (decryptKey == null || decryptKey.Length == 0)
					continue;

				if (decrypterType.Detected) {
					var data = new byte[8];
					ulong magic = decrypterType.getMagic();
					data[0] = (byte)magic;
					data[7] = (byte)(magic >> 8);
					data[6] = (byte)(magic >> 16);
					data[5] = (byte)(magic >> 24);
					data[4] = (byte)(magic >> 32);
					data[1] = (byte)(magic >> 40);
					data[3] = (byte)(magic >> 48);
					data[2] = (byte)(magic >> 56);

					for (int i = 0; i < decryptKey.Length; i++)
						decryptKey[i] ^= (byte)(i + data[i % data.Length]);
				}

				return true;
			}

			return false;
		}

		static byte[] decodeBase64(string s) {
			try {
				return Convert.FromBase64String(s);
			}
			catch (FormatException) {
				return null;
			}
		}

		bool createAssemblyInfos() {
			int numElements = DeobUtils.hasInteger(handlerMethod, 3) ? 3 : 2;
			foreach (var s in DotNetUtils.getCodeStrings(handlerMethod)) {
				var infos = createAssemblyInfos(s, numElements);
				if (infos == null)
					continue;

				assemblyInfos = infos;
				return true;
			}

			return false;
		}

		List<AssemblyInfo> createAssemblyInfos(string s, int numElements) {
			try {
				return tryCreateAssemblyInfos(s, numElements);
			}
			catch (FormatException) {
				return null;	// Convert.FromBase64String() failed
			}
		}

		List<AssemblyInfo> tryCreateAssemblyInfos(string s, int numElements) {
			var ary = s.Split(',');
			if (ary.Length == 0 || ary.Length % numElements != 0)
				return null;

			var infos = new List<AssemblyInfo>();
			for (int i = 0; i < ary.Length; i += numElements) {
				var info = new AssemblyInfo();

				info.AssemblyFullName = Encoding.UTF8.GetString(Convert.FromBase64String(ary[i]));
				info.ResourceName = ary[i + 1];
				if (numElements >= 3)
					info.Filename = Encoding.UTF8.GetString(Convert.FromBase64String(ary[i + 2]));
				else
					info.Filename = Utils.getAssemblySimpleName(info.AssemblyFullName) + ".dll";
				int index = info.ResourceName.IndexOf('|');
				if (index >= 0) {
					var flags = info.ResourceName.Substring(0, index);
					info.ResourceName = info.ResourceName.Substring(index + 1);
					info.IsEncrypted = flags.IndexOf('a') >= 0;
					info.IsCompressed = flags.IndexOf('b') >= 0;
				}

				infos.Add(info);
			}

			return infos;
		}

		public void initializeEmbeddedFiles() {
			foreach (var info in assemblyInfos) {
				info.Resource = DotNetUtils.getResource(module, info.ResourceName) as EmbeddedResource;
				if (info.Resource == null)
					throw new ApplicationException(string.Format("Could not find resource {0}", Utils.toCsharpString(info.ResourceName)));

				info.Data = info.Resource.GetResourceData();
				if (info.IsEncrypted)
					decrypt(info.Data);
				if (info.IsCompressed)
					info.Data = decompress(info.Data);

				initializeNameAndExtension(info);
			}
		}

		static void initializeNameAndExtension(AssemblyInfo info) {
			try {
				var mod = ModuleDefMD.Load(info.Data);
				info.AssemblyFullName = mod.Assembly.FullName;
				info.SimpleName = mod.Assembly.Name.String;
				info.Extension = DeobUtils.getExtension(mod.Kind);
				return;
			}
			catch {
			}
			Logger.w("Could not load assembly from decrypted resource {0}", Utils.toCsharpString(info.ResourceName));
			int index = info.Filename.LastIndexOf('.');
			if (index < 0) {
				info.SimpleName = info.Filename;
				info.Extension = "";
			}
			else {
				info.SimpleName = info.Filename.Substring(0, index);
				info.Extension = info.Filename.Substring(index);
			}
		}

		static readonly byte[] key2 = new byte[] { 148, 68, 208, 52 };
		void decrypt(byte[] encryptedData) {
			var indexes = new byte[256];
			for (int i = 0; i < indexes.Length; i++)
				indexes[i] = (byte)i;
			byte i1 = 0, i2 = 0;
			for (int i = 0; i < indexes.Length; i++) {
				i2 += (byte)(decryptKey[i % decryptKey.Length] + indexes[i]);
				swap(indexes, i, i2);
			}

			byte val = 0;
			for (int i = 0; i < encryptedData.Length; i++) {
				if ((i & 0x1F) == 0) {
					i2 += indexes[++i1];
					swap(indexes, i1, i2);
					val = indexes[(byte)(indexes[i1] + indexes[i2])];
				}
				encryptedData[i] ^= (byte)(val ^ key2[(i >> 2) & 3] ^ key2[(i + 1) & 3]);
			}
		}

		static void swap(byte[] data, int i, int j) {
			byte tmp = data[i];
			data[i] = data[j];
			data[j] = tmp;
		}

		byte[] decompress(byte[] compressedData) {
			// First dword is sig: 0x9B728BC7
			// Second dword is decompressed length
			return DeobUtils.inflate(compressedData, 8, compressedData.Length - 8, true);
		}

		public AssemblyInfo get(string asmFullName) {
			var simpleName = Utils.getAssemblySimpleName(asmFullName);
			for (int i = 0; i < assemblyInfos.Count; i++) {
				var info = assemblyInfos[i];
				if (info.SimpleName != simpleName)
					continue;

				assemblyInfos.RemoveAt(i);
				return info;
			}

			return null;
		}

		public void deobfuscate(Blocks blocks) {
			codeCompilerMethodCallRestorer.deobfuscate(blocks);
		}

		void findCodeDomMethods() {
			if (resolverType == null)
				return;

			foreach (var nestedType in resolverType.NestedTypes) {
				if (nestedType.Fields.Count != 0)
					continue;

				var CompileAssemblyFromDom1         = getTheOnlyMethod(nestedType, "System.CodeDom.Compiler.CodeDomProvider", "CompileAssemblyFromDom", "System.CodeDom.Compiler.CompilerResults", "System.CodeDom.Compiler.CompilerParameters,System.CodeDom.CodeCompileUnit[]");
				var CompileAssemblyFromFile1        = getTheOnlyMethod(nestedType, "System.CodeDom.Compiler.CodeDomProvider", "CompileAssemblyFromFile", "System.CodeDom.Compiler.CompilerResults", "System.CodeDom.Compiler.CompilerParameters,System.String[]");
				var CompileAssemblyFromSource1      = getTheOnlyMethod(nestedType, "System.CodeDom.Compiler.CodeDomProvider", "CompileAssemblyFromSource", "System.CodeDom.Compiler.CompilerResults", "System.CodeDom.Compiler.CompilerParameters,System.String[]");
				var CompileAssemblyFromDom2         = getTheOnlyMethod(nestedType, "System.CodeDom.Compiler.ICodeCompiler", "CompileAssemblyFromDom", "System.CodeDom.Compiler.CompilerResults", "System.CodeDom.Compiler.CompilerParameters,System.CodeDom.CodeCompileUnit");
				var CompileAssemblyFromDomBatch2    = getTheOnlyMethod(nestedType, "System.CodeDom.Compiler.ICodeCompiler", "CompileAssemblyFromDomBatch", "System.CodeDom.Compiler.CompilerResults", "System.CodeDom.Compiler.CompilerParameters,System.CodeDom.CodeCompileUnit[]");
				var CompileAssemblyFromFile2        = getTheOnlyMethod(nestedType, "System.CodeDom.Compiler.ICodeCompiler", "CompileAssemblyFromFile", "System.CodeDom.Compiler.CompilerResults", "System.CodeDom.Compiler.CompilerParameters,System.String");
				var CompileAssemblyFromFileBatch2   = getTheOnlyMethod(nestedType, "System.CodeDom.Compiler.ICodeCompiler", "CompileAssemblyFromFileBatch", "System.CodeDom.Compiler.CompilerResults", "System.CodeDom.Compiler.CompilerParameters,System.String[]");
				var CompileAssemblyFromSource2      = getTheOnlyMethod(nestedType, "System.CodeDom.Compiler.ICodeCompiler", "CompileAssemblyFromSource", "System.CodeDom.Compiler.CompilerResults", "System.CodeDom.Compiler.CompilerParameters,System.String");
				var CompileAssemblyFromSourceBatch2 = getTheOnlyMethod(nestedType, "System.CodeDom.Compiler.ICodeCompiler", "CompileAssemblyFromSourceBatch", "System.CodeDom.Compiler.CompilerResults", "System.CodeDom.Compiler.CompilerParameters,System.String[]");

				if (CompileAssemblyFromDom1 == null && CompileAssemblyFromFile1 == null &&
					CompileAssemblyFromSource1 == null && CompileAssemblyFromDom2 == null &&
					CompileAssemblyFromDomBatch2 == null && CompileAssemblyFromFile2 == null &&
					CompileAssemblyFromFileBatch2 == null && CompileAssemblyFromSource2 == null &&
					CompileAssemblyFromSourceBatch2 == null) {
					continue;
				}

				codeCompilerMethodCallRestorer.add_CodeDomProvider_CompileAssemblyFromDom(CompileAssemblyFromDom1);
				codeCompilerMethodCallRestorer.add_CodeDomProvider_CompileAssemblyFromFile(CompileAssemblyFromFile1);
				codeCompilerMethodCallRestorer.add_CodeDomProvider_CompileAssemblyFromSource(CompileAssemblyFromSource1);
				codeCompilerMethodCallRestorer.add_ICodeCompiler_CompileAssemblyFromDom(CompileAssemblyFromDom2);
				codeCompilerMethodCallRestorer.add_ICodeCompiler_CompileAssemblyFromDomBatch(CompileAssemblyFromDomBatch2);
				codeCompilerMethodCallRestorer.add_ICodeCompiler_CompileAssemblyFromFile(CompileAssemblyFromFile2);
				codeCompilerMethodCallRestorer.add_ICodeCompiler_CompileAssemblyFromFileBatch(CompileAssemblyFromFileBatch2);
				codeCompilerMethodCallRestorer.add_ICodeCompiler_CompileAssemblyFromSource(CompileAssemblyFromSource2);
				codeCompilerMethodCallRestorer.add_ICodeCompiler_CompileAssemblyFromSourceBatch(CompileAssemblyFromSourceBatch2);
				break;
			}
		}

		static MethodDef getTheOnlyMethod(TypeDef type, string typeName, string methodName, string returnType, string parameters) {
			MethodDef foundMethod = null;

			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null || method.HasGenericParameters)
					continue;
				if (method.IsPrivate)
					continue;
				if (!DotNetUtils.isMethod(method, returnType, "(" + typeName + "," + parameters + ")"))
					continue;
				if (!DotNetUtils.callsMethod(method, returnType + " " + typeName + "::" + methodName + "(" + parameters + ")"))
					continue;

				if (foundMethod != null)
					return null;
				foundMethod = method;
			}

			return foundMethod;
		}
	}
}
