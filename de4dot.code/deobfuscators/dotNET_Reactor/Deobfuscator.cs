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

using System.IO;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.MyStuff;
using de4dot.blocks;

namespace de4dot.deobfuscators.dotNET_Reactor {
	class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = ".NET Reactor";
		const string DEFAULT_REGEX = @"!^[a-zA-Z0-9]{6,}&" + DeobfuscatorBase.DEFAULT_VALID_NAME_REGEX;
		BoolOption decryptMethods;
		BoolOption decryptBools;
		BoolOption restoreTypes;
		BoolOption inlineMethods;
		BoolOption removeInlinedMethods;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
			decryptMethods = new BoolOption(null, makeArgName("methods"), "Decrypt methods", true);
			decryptBools = new BoolOption(null, makeArgName("bools"), "Decrypt booleans", true);
			restoreTypes = new BoolOption(null, makeArgName("types"), "Restore types (object -> real type)", true);
			inlineMethods = new BoolOption(null, makeArgName("inline"), "Inline short methods", true);
			removeInlinedMethods = new BoolOption(null, makeArgName("remove-inlined"), "Remove inlined methods", true);
		}

		public override string Name {
			get { return THE_NAME; }
		}

		public override string Type {
			get { return "dr"; }
		}

		public override IDeobfuscator createDeobfuscator() {
			return new Deobfuscator(new Deobfuscator.Options {
				ValidNameRegex = validNameRegex.get(),
				DecryptMethods = decryptMethods.get(),
				DecryptBools = decryptBools.get(),
				RestoreTypes = restoreTypes.get(),
				InlineMethods = inlineMethods.get(),
				RemoveInlinedMethods = removeInlinedMethods.get(),
			});
		}

		protected override IEnumerable<Option> getOptionsInternal() {
			return new List<Option>() {
				decryptMethods,
				decryptBools,
				restoreTypes,
				inlineMethods,
				removeInlinedMethods,
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		string obfuscatorName = ".NET Reactor";

		PE.PeImage peImage;
		byte[] fileData;
		MethodsDecrypter methodsDecrypter;
		StringDecrypter stringDecrypter;
		BooleanDecrypter booleanDecrypter;
		BoolValueInliner boolValueInliner;
		MetadataTokenObfuscator metadataTokenObfuscator;

		bool canRemoveDecrypterType = true;
		bool startedDeobfuscating = false;

		internal class Options : OptionsBase {
			public bool DecryptMethods { get; set; }
			public bool DecryptBools { get; set; }
			public bool RestoreTypes { get; set; }
			public bool InlineMethods { get; set; }
			public bool RemoveInlinedMethods { get; set; }
		}

		public override string Type {
			get { return DeobfuscatorInfo.THE_NAME; }
		}

		public override string Name {
			get { return obfuscatorName; }
		}

		public override bool CanInlineMethods {
			get { return startedDeobfuscating ? options.InlineMethods : true; }
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
		}

		public override void init(ModuleDefinition module) {
			base.init(module);
		}

		protected override int detectInternal() {
			int val = 0;

			if (methodsDecrypter.Detected || stringDecrypter.Detected || booleanDecrypter.Detected)
				val += 100;

			int sum = convert(methodsDecrypter.Detected) +
					convert(stringDecrypter.Detected) +
					convert(booleanDecrypter.Detected);
			if (sum > 1)
				val += 10 * (sum - 1);

			return val;
		}

		static int convert(bool b) {
			return b ? 1 : 0;
		}

		protected override void scanForObfuscator() {
			methodsDecrypter = new MethodsDecrypter(module);
			methodsDecrypter.find();
			stringDecrypter = new StringDecrypter(module);
			stringDecrypter.find(DeobfuscatedFile);
			booleanDecrypter = new BooleanDecrypter(module);
			booleanDecrypter.find();
			obfuscatorName = detectVersion();
		}

		string detectVersion() {
			/*
			Methods decrypter locals (not showing its own types):
			3.7.0.3:
					"System.Byte[]"
					"System.Int32"
					"System.Int32[]"
					"System.IntPtr"
					"System.IO.BinaryReader"
					"System.IO.MemoryStream"
					"System.Object"
					"System.Reflection.Assembly"
					"System.Security.Cryptography.CryptoStream"
					"System.Security.Cryptography.ICryptoTransform"
					"System.Security.Cryptography.RijndaelManaged"
					"System.String"

			3.9.8.0:
			-		"System.Int32[]"
			+		"System.Diagnostics.StackFrame"

			4.0.0.0: (jitter)
			-		"System.Diagnostics.StackFrame"
			-		"System.Object"
			+		"System.Boolean"
			+		"System.Collections.IEnumerator"
			+		"System.Delegate"
			+		"System.Diagnostics.Process"
			+		"System.Diagnostics.ProcessModule"
			+		"System.Diagnostics.ProcessModuleCollection"
			+		"System.IDisposable"
			+		"System.Int64"
			+		"System.UInt32"
			+		"System.UInt64"

			4.1.0.0: (jitter)
			+		"System.Reflection.Assembly"

			4.3.1.0: (jitter)
			+		"System.Byte&"
			*/

			LocalTypes localTypes;
			int minVer = -1;
			foreach (var info in stringDecrypter.DecrypterInfos) {
				if (info.key == null)
					continue;
				localTypes = new LocalTypes(info.method);
				if (!localTypes.exists("System.IntPtr"))
					return ".NET Reactor <= 3.7";
				minVer = 3800;
				break;
			}

			if (methodsDecrypter.MethodsDecrypterMethod == null) {
				if (minVer >= 3800)
					return ".NET Reactor >= 3.8";
				return ".NET Reactor";
			}
			localTypes = new LocalTypes(methodsDecrypter.MethodsDecrypterMethod);

			if (localTypes.exists("System.Int32[]")) {
				if (minVer >= 3800)
					return ".NET Reactor 3.8.4.1 - 3.9.0.1";
				return ".NET Reactor <= 3.9.0.1";
			}
			if (!localTypes.exists("System.Diagnostics.Process")) {	// If < 4.0
				if (localTypes.exists("System.Diagnostics.StackFrame"))
					return ".NET Reactor 3.9.8.0";
			}

			var compileMethod = MethodsDecrypter.findDnrCompileMethod(methodsDecrypter.MethodsDecrypterMethod.DeclaringType);
			if (compileMethod == null)
				return ".NET Reactor < 4.0";
			DeobfuscatedFile.deobfuscate(compileMethod);
			bool compileMethodHasConstant_0x70000000 = findConstant(compileMethod, 0x70000000);	// 4.0-4.1
			DeobfuscatedFile.deobfuscate(methodsDecrypter.MethodsDecrypterMethod);
			bool hasCorEnableProfilingString = findString(methodsDecrypter.MethodsDecrypterMethod, "Cor_Enable_Profiling");	// 4.1-4.4

			if (compileMethodHasConstant_0x70000000) {
				if (hasCorEnableProfilingString)
					return ".NET Reactor 4.1";
				return ".NET Reactor 4.0";
			}
			if (!hasCorEnableProfilingString)
				return ".NET Reactor";
			// 4.2-4.4

			if (!localTypes.exists("System.Byte&"))
				return ".NET Reactor 4.2";

			localTypes = new LocalTypes(compileMethod);
			if (localTypes.exists("System.Object"))
				return ".NET Reactor 4.4";
			return ".NET Reactor 4.3";
		}

		static bool findString(MethodDefinition method, string s) {
			foreach (var cs in DotNetUtils.getCodeStrings(method)) {
				if (cs == s)
					return true;
			}
			return false;
		}

		static bool findConstant(MethodDefinition method, int constant) {
			if (method == null || method.Body == null)
				return false;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Ldc_I4)
					continue;
				if (constant == (int)instr.Operand)
					return true;
			}
			return false;
		}

		public override bool getDecryptedModule(ref byte[] newFileData, ref Dictionary<uint, DumpedMethod> dumpedMethods) {
			using (var fileStream = new FileStream(module.FullyQualifiedName, FileMode.Open, FileAccess.Read, FileShare.Read)) {
				fileData = new byte[(int)fileStream.Length];
				fileStream.Read(fileData, 0, fileData.Length);
			}
			peImage = new PE.PeImage(fileData);

			if (!options.DecryptMethods)
				return false;

			if (!methodsDecrypter.decrypt(peImage, DeobfuscatedFile, ref dumpedMethods))
				return false;

			newFileData = fileData;
			return true;
		}

		public override IDeobfuscator moduleReloaded(ModuleDefinition module) {
			var newOne = new Deobfuscator(options);
			newOne.setModule(module);
			newOne.fileData = fileData;
			newOne.peImage = new PE.PeImage(fileData);
			newOne.methodsDecrypter = new MethodsDecrypter(module, methodsDecrypter);
			newOne.stringDecrypter = new StringDecrypter(module, stringDecrypter);
			newOne.booleanDecrypter = new BooleanDecrypter(module, booleanDecrypter);
			return newOne;
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();

			stringDecrypter.init(peImage, fileData, DeobfuscatedFile);
			booleanDecrypter.init(fileData, DeobfuscatedFile);
			boolValueInliner = new BoolValueInliner();

			if (options.DecryptBools) {
				boolValueInliner.add(booleanDecrypter.BoolDecrypterMethod, (method, args) => {
					return booleanDecrypter.decrypt((int)args[0]);
				});
			}

			foreach (var info in stringDecrypter.DecrypterInfos) {
				staticStringDecrypter.add(info.method, (method2, args) => {
					return stringDecrypter.decrypt(method2, (int)args[0]);
				});
			}
			if (stringDecrypter.OtherStringDecrypter != null) {
				staticStringDecrypter.add(stringDecrypter.OtherStringDecrypter, (method2, args) => {
					return stringDecrypter.decrypt((string)args[0]);
				});
			}
			DeobfuscatedFile.stringDecryptersAdded();

			metadataTokenObfuscator = new MetadataTokenObfuscator(module);

			if (Operations.DecryptStrings != OpDecryptString.None)
				addResourceToBeRemoved(stringDecrypter.StringsResource, "Encrypted strings");
			if (options.DecryptMethods) {
				addResourceToBeRemoved(methodsDecrypter.MethodsResource, "Encrypted methods");
				addCctorInitCallToBeRemoved(methodsDecrypter.MethodsDecrypterMethod);
			}
			if (options.DecryptBools)
				addResourceToBeRemoved(booleanDecrypter.BooleansResource, "Encrypted booleans");
			bool deleteTypes = Operations.DecryptStrings != OpDecryptString.None &&
								options.DecryptMethods &&
								options.DecryptBools &&
								options.InlineMethods;
			if (!deleteTypes)
				canRemoveDecrypterType = false;

			// Set it to false until we've removed all references to it
			canRemoveDecrypterType = false;

			startedDeobfuscating = true;
		}

		public override bool deobfuscateOther(Blocks blocks) {
			if (boolValueInliner.HasHandlers)
				return boolValueInliner.decrypt(blocks) > 0;
			return false;
		}

		public override void deobfuscateMethodEnd(Blocks blocks) {
			metadataTokenObfuscator.deobfuscate(blocks);
			base.deobfuscateMethodEnd(blocks);
		}

		public override void deobfuscateEnd() {
			removeInlinedMethods();
			if (options.RestoreTypes)
				new TypesRestorer(module).deobfuscate();
			if (canRemoveDecrypterType && methodsDecrypter.MethodsDecrypterMethod != null) {
				addTypeToBeRemoved(methodsDecrypter.MethodsDecrypterMethod.DeclaringType, "Decrypter type");
				addTypeToBeRemoved(metadataTokenObfuscator.Type, "Metadata token obfuscator");
			}
			base.deobfuscateEnd();
		}

		class UnusedMethodsFinder {
			ModuleDefinition module;
			Dictionary<MethodDefinition, bool> possiblyUnusedMethods = new Dictionary<MethodDefinition, bool>();
			Stack<MethodDefinition> notUnusedStack = new Stack<MethodDefinition>();

			public UnusedMethodsFinder(ModuleDefinition module, IEnumerable<MethodDefinition> possiblyUnusedMethods) {
				this.module = module;
				foreach (var method in possiblyUnusedMethods)
					this.possiblyUnusedMethods[method] = true;
			}

			public IEnumerable<MethodDefinition> find() {
				if (possiblyUnusedMethods.Count == 0)
					return possiblyUnusedMethods.Keys;

				foreach (var type in module.GetTypes()) {
					foreach (var method in type.Methods)
						check(method);
				}

				while (notUnusedStack.Count > 0) {
					var method = notUnusedStack.Pop();
					if (!possiblyUnusedMethods.Remove(method))
						continue;
					check(method);
				}

				return possiblyUnusedMethods.Keys;
			}

			void check(MethodDefinition method) {
				if (method.Body == null)
					return;
				if (possiblyUnusedMethods.ContainsKey(method))
					return;

				foreach (var instr in method.Body.Instructions) {
					switch (instr.OpCode.Code) {
					case Code.Call:
					case Code.Calli:
					case Code.Callvirt:
					case Code.Newobj:
					case Code.Ldtoken:
						break;
					default:
						continue;
					}

					var calledMethod = DotNetUtils.getMethod(module, instr.Operand as MethodReference);
					if (calledMethod == null)
						continue;
					if (possiblyUnusedMethods.ContainsKey(calledMethod))
						notUnusedStack.Push(calledMethod);
				}
			}
		}

		void removeInlinedMethods() {
			if (!options.InlineMethods || !options.RemoveInlinedMethods)
				return;

			// Not all garbage methods are inlined, possibly because we remove some code that calls
			// the garbage method before the methods inliner has a chance to inline it. Try to find
			// all garbage methods and other code will figure out if there are any calls left.

			var inlinedMethods = new List<MethodDefinition>();
			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					if (!method.IsStatic || !method.IsAssembly)
						continue;
					if (method.Body == null)
						continue;
					var instrs = method.Body.Instructions;
					if (instrs.Count < 2)
						continue;

					switch (instrs[0].OpCode.Code) {
					case Code.Ldc_I4:
					case Code.Ldc_I4_0:
					case Code.Ldc_I4_1:
					case Code.Ldc_I4_2:
					case Code.Ldc_I4_3:
					case Code.Ldc_I4_4:
					case Code.Ldc_I4_5:
					case Code.Ldc_I4_6:
					case Code.Ldc_I4_7:
					case Code.Ldc_I4_8:
					case Code.Ldc_I4_M1:
					case Code.Ldc_I4_S:
					case Code.Ldc_I8:
					case Code.Ldc_R4:
					case Code.Ldc_R8:
					case Code.Ldftn:
					case Code.Ldnull:
					case Code.Ldstr:
					case Code.Ldtoken:
					case Code.Ldsfld:
					case Code.Ldsflda:
						if (instrs[1].OpCode.Code != Code.Ret)
							continue;
						break;

					case Code.Ldarg:
					case Code.Ldarg_S:
					case Code.Ldarg_0:
					case Code.Ldarg_1:
					case Code.Ldarg_2:
					case Code.Ldarg_3:
					case Code.Call:
						if (!isCallMethod(method))
							continue;
						break;

					default:
						continue;
					}

					inlinedMethods.Add(method);
				}
			}
			addMethodsToBeRemoved(new UnusedMethodsFinder(module, inlinedMethods).find(), "Inlined method");
		}

		bool isCallMethod(MethodDefinition method) {
			int loadIndex = 0;
			int methodArgsCount = DotNetUtils.getArgsCount(method);
			var instrs = method.Body.Instructions;
			int i = 0;
			for (; i < instrs.Count; i++) {
				var instr = instrs[i];
				switch (instr.OpCode.Code) {
				case Code.Ldarg:
				case Code.Ldarg_S:
				case Code.Ldarg_0:
				case Code.Ldarg_1:
				case Code.Ldarg_2:
				case Code.Ldarg_3:
					if (DotNetUtils.getArgIndex(method, instr) != loadIndex)
						return false;
					loadIndex++;
					continue;
				}
				break;
			}
			if (loadIndex != methodArgsCount)
				return false;
			if (i + 1 >= instrs.Count)
				return false;

			if (instrs[i].OpCode.Code != Code.Call && instrs[i].OpCode.Code != Code.Callvirt)
				return false;
			if (instrs[i + 1].OpCode.Code != Code.Ret)
				return false;

			return true;
		}

		public override IEnumerable<string> getStringDecrypterMethods() {
			var list = new List<string>();
			foreach (var info in stringDecrypter.DecrypterInfos)
				list.Add(info.method.MetadataToken.ToInt32().ToString("X8"));
			if (stringDecrypter.OtherStringDecrypter != null)
				list.Add(stringDecrypter.OtherStringDecrypter.MetadataToken.ToInt32().ToString("X8"));
			return list;
		}
	}
}
