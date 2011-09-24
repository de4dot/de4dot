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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.deobfuscators;
using de4dot.blocks;
using de4dot.AssemblyClient;

namespace de4dot {
	class ObfuscatedFile : IObfuscatedFile, IDeobfuscatedFile {
		Options options;
		ModuleDefinition module;
		IList<MethodDefinition> allMethods;
		IDeobfuscator deob;
		AssemblyModule assemblyModule;
		IAssemblyClient assemblyClient;
		DynamicStringDecrypter dynamicStringDecrypter;
		IAssemblyClientFactory assemblyClientFactory;
		SavedMethodBodies savedMethodBodies;
		bool userStringDecrypterMethods = false;

		class SavedMethodBodies {
			Dictionary<MethodDefinition, SavedMethodBody> savedMethodBodies = new Dictionary<MethodDefinition, SavedMethodBody>();

			class SavedMethodBody {
				MethodDefinition method;
				IList<Instruction> instructions;
				IList<ExceptionHandler> exceptionHandlers;

				public SavedMethodBody(MethodDefinition method) {
					this.method = method;
					DotNetUtils.copyBody(method, out instructions, out exceptionHandlers);
				}

				public void restore() {
					DotNetUtils.restoreBody(method, instructions, exceptionHandlers);
				}
			}

			public void save(MethodDefinition method) {
				if (isSaved(method))
					return;
				savedMethodBodies[method] = new SavedMethodBody(method);
			}

			public void restoreAll() {
				foreach (var smb in savedMethodBodies.Values)
					smb.restore();
				savedMethodBodies.Clear();
			}

			public bool isSaved(MethodDefinition method) {
				return savedMethodBodies.ContainsKey(method);
			}
		}

		public class Options {
			public string Filename { get; set; }
			public string MethodsFilename { get; set; }
			public string NewFilename { get; set; }
			public string ForcedObfuscatorType { get; set; }
			public DecrypterType StringDecrypterType { get; set; }
			public List<string> StringDecrypterMethods { get; private set; }
			public bool RenameSymbols { get; set; }
			public bool ControlFlowDeobfuscation { get; set; }
			public bool KeepObfuscatorTypes { get; set; }

			public Options() {
				StringDecrypterType = DecrypterType.Static;
				StringDecrypterMethods = new List<string>();
			}
		}

		public string Filename {
			get { return options.Filename; }
		}

		public string NewFilename {
			get { return options.NewFilename; }
		}

		public ModuleDefinition ModuleDefinition {
			get { return module; }
		}

		public Func<string, bool> IsValidName {
			get { return deob.IsValidName; }
		}

		public bool RenameResourcesInCode {
			get { return deob.TheOptions.RenameResourcesInCode; }
		}

		public bool RenameSymbols {
			get { return options.RenameSymbols; }
		}

		public IDeobfuscator Deobfuscator {
			get { return deob; }
		}

		public ObfuscatedFile(Options options, IAssemblyClientFactory assemblyClientFactory) {
			this.assemblyClientFactory = assemblyClientFactory;
			this.options = options;
			userStringDecrypterMethods = options.StringDecrypterMethods.Count > 0;
			options.Filename = Utils.getFullPath(options.Filename);
			assemblyModule = new AssemblyModule(options.Filename, options.MethodsFilename);

			if (options.NewFilename == null)
				options.NewFilename = getDefaultNewFilename();

			if (string.Equals(options.Filename, options.NewFilename, StringComparison.OrdinalIgnoreCase))
				throw new UserException(string.Format("filename is same as new filename! ({0})", options.Filename));
		}

		string getDefaultNewFilename() {
			int dotIndex = options.Filename.LastIndexOf('.');
			string noExt, ext;
			if (dotIndex != -1) {
				noExt = options.Filename.Substring(0, dotIndex);
				ext = options.Filename.Substring(dotIndex);
			}
			else {
				noExt = options.Filename;
				ext = "";
			}
			return noExt + "-fixed" + ext;
		}

		public void load(IEnumerable<IDeobfuscator> deobfuscators) {
			module = assemblyModule.load();
			AssemblyResolver.Instance.addSearchDirectory(Utils.getDirName(Filename));
			AssemblyResolver.Instance.addSearchDirectory(Utils.getDirName(NewFilename));

			allMethods = getAllMethods();

			detectObfuscator(deobfuscators);
			if (deob == null)
				throw new ApplicationException("Could not detect obfuscator!");

			deob.Operations = createOperations();
		}

		IOperations createOperations() {
			var op = new Operations();

			if (options.StringDecrypterType == DecrypterType.None)
				op.DecryptStrings = OpDecryptString.None;
			else if (options.StringDecrypterType == DecrypterType.Static)
				op.DecryptStrings = OpDecryptString.Static;
			else
				op.DecryptStrings = OpDecryptString.Dynamic;

			op.RenameSymbols = options.RenameSymbols;
			op.KeepObfuscatorTypes = options.KeepObfuscatorTypes;

			return op;
		}

		void detectObfuscator(IEnumerable<IDeobfuscator> deobfuscators) {
			IList<MemberReference> memberReferences = new List<MemberReference>(module.GetMemberReferences());

			// The deobfuscators may call methods to deobfuscate control flow and decrypt
			// strings (statically) in order to detect the obfuscator.
			if (!options.ControlFlowDeobfuscation || options.StringDecrypterType == DecrypterType.None)
				savedMethodBodies = new SavedMethodBodies();

			foreach (var deob in deobfuscators) {
				deob.DeobfuscatedFile = this;
				deob.init(module, memberReferences);
			}

			if (options.ForcedObfuscatorType != null) {
				foreach (var deob in deobfuscators) {
					if (string.Equals(options.ForcedObfuscatorType, deob.Type, StringComparison.OrdinalIgnoreCase)) {
						this.deob = deob;
						return;
					}
				}
			}
			else {
				int detectVal = 0;
				foreach (var deob in deobfuscators) {
					int val = deob.detect();
					Log.v("{0,3}: {1}", val, deob.Type);
					if (val > detectVal) {
						detectVal = val;
						this.deob = deob;
					}
				}
			}
		}

		public void save() {
			Log.n("Saving {0}", options.NewFilename);
			assemblyModule.save(options.NewFilename);
		}

		IList<MethodDefinition> getAllMethods() {
			var list = new List<MethodDefinition>();

			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods)
					list.Add(method);
			}

			return list;
		}

		public void deobfuscateBegin() {
			switch (options.StringDecrypterType) {
			case DecrypterType.None:
				checkSupportedStringDecrypter(StringFeatures.AllowNoDecryption);
				break;

			case DecrypterType.Static:
				checkSupportedStringDecrypter(StringFeatures.AllowStaticDecryption);
				break;

			case DecrypterType.Delegate:
			case DecrypterType.Emulate:
				checkSupportedStringDecrypter(StringFeatures.AllowDynamicDecryption);
				assemblyClient = assemblyClientFactory.create();
				assemblyClient.connect();
				break;

			default:
				throw new ApplicationException(string.Format("Invalid string decrypter type '{0}'", options.StringDecrypterType));
			}
		}

		public void checkSupportedStringDecrypter(StringFeatures feature) {
			if ((deob.StringFeatures & feature) == feature)
				return;
			throw new UserException(string.Format("Deobfuscator {0} does not support this string decryption type", deob.Type));
		}

		public void deobfuscate() {
			Log.n("Cleaning {0}", options.Filename);
			initAssemblyClient();
			deob.deobfuscateBegin();
			deobfuscateMethods();
			deob.deobfuscateEnd();
		}

		void initAssemblyClient() {
			if (assemblyClient == null)
				return;

			assemblyClient.waitConnected();
			assemblyClient.Service.loadAssembly(options.Filename);

			if (options.StringDecrypterType == DecrypterType.Delegate)
				assemblyClient.Service.setStringDecrypterType(AssemblyData.StringDecrypterType.Delegate);
			else if (options.StringDecrypterType == DecrypterType.Emulate)
				assemblyClient.Service.setStringDecrypterType(AssemblyData.StringDecrypterType.Emulate);
			else
				throw new ApplicationException(string.Format("Invalid string decrypter type '{0}'", options.StringDecrypterType));

			dynamicStringDecrypter = new DynamicStringDecrypter(assemblyClient);
			updateDynamicStringDecrypter();
		}

		void updateDynamicStringDecrypter() {
			if (dynamicStringDecrypter != null)
				dynamicStringDecrypter.init(getMethodTokens());
		}

		IEnumerable<int> getMethodTokens() {
			var tokens = new List<int>();

			if (!userStringDecrypterMethods) {
				options.StringDecrypterMethods.Clear();
				options.StringDecrypterMethods.AddRange(deob.getStringDecrypterMethods());
			}

			foreach (var val in options.StringDecrypterMethods) {
				var tokenStr = val.Trim();
				if (tokenStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
					tokenStr = tokenStr.Substring(2);
				int methodToken;
				if (int.TryParse(tokenStr, NumberStyles.HexNumber, null, out methodToken))
					tokens.Add(methodToken);
				else
					tokens.AddRange(findMethodTokens(val));
			}

			return tokens;
		}

		IEnumerable<int> findMethodTokens(string methodDesc) {
			var tokens = new List<int>();

			string typeString, methodName;
			string[] argsStrings;
			splitMethodDesc(methodDesc, out typeString, out methodName, out argsStrings);

			foreach (var type in module.GetTypes()) {
				if (typeString != null && typeString != type.FullName)
					continue;
				foreach (var method in type.Methods) {
					if (!method.IsStatic || method.MethodReturnType.ReturnType.FullName != "System.String")
						continue;
					if (methodName != null && methodName != method.Name)
						continue;

					if (argsStrings == null) {
						if (method.Parameters.Count == 0)
							continue;
					}
					else {
						if (argsStrings.Length != method.Parameters.Count)
							continue;
						for (int i = 0; i < argsStrings.Length; i++) {
							if (argsStrings[i] != method.Parameters[i].ParameterType.FullName)
								continue;
						}
					}

					Log.v("Adding string decrypter; token: {0:X8}, method: {1}", method.MetadataToken.ToInt32(), method.FullName);
					tokens.Add(method.MetadataToken.ToInt32());
				}
			}

			return tokens;
		}

		static void splitMethodDesc(string methodDesc, out string type, out string name, out string[] args) {
			string stringArgs = null;
			args = null;
			type = null;
			name = null;

			var remaining = methodDesc;
			int index = remaining.LastIndexOf("::");
			if (index >= 0) {
				type = remaining.Substring(0, index);
				remaining = remaining.Substring(index + 2);
			}

			index = remaining.IndexOf('(');
			if (index >= 0) {
				name = remaining.Substring(0, index);
				remaining = remaining.Substring(index);
			}
			else {
				name = remaining;
				remaining = "";
			}

			if (remaining.StartsWith("(", StringComparison.Ordinal)) {
				stringArgs = remaining;
			}
			else if (remaining.Length > 0)
				throw new UserException(string.Format("Invalid method desc: '{0}'", methodDesc));

			if (stringArgs != null) {
				if (stringArgs.StartsWith("(", StringComparison.Ordinal))
					stringArgs = stringArgs.Substring(1);
				if (stringArgs.EndsWith(")", StringComparison.Ordinal))
					stringArgs = stringArgs.Substring(0, stringArgs.Length - 1);
				args = stringArgs.Split(',');
				for (int i = 0; i < args.Length; i++)
					args[i] = args[i].Trim();
			}

			if (type == "")
				type = null;
			if (name == "")
				name = null;
		}

		public void deobfuscateEnd() {
			deobfuscateCleanUp();
		}

		public void deobfuscateCleanUp() {
			if (assemblyClient != null) {
				assemblyClient.Dispose();
				assemblyClient = null;
			}
		}

		void deobfuscateMethods() {
			if (savedMethodBodies != null) {
				savedMethodBodies.restoreAll();
				savedMethodBodies = null;
			}
			deob.DeobfuscatedFile = null;

			Log.v("Deobfuscating methods");
			foreach (var method in allMethods) {
				Log.v("Deobfuscating {0} ({1:X8})", method, method.MetadataToken.ToUInt32());
				Log.indent();

				if (method.HasBody) {
					var blocks = new Blocks(method);

					deob.deobfuscateMethodBegin(blocks);
					if (options.ControlFlowDeobfuscation) {
						int numDeadBlocks = blocks.deobfuscate();
						if (numDeadBlocks > 0)
							Log.v("Removed {0} dead block(s)", numDeadBlocks);
					}
					deobfuscateStrings(blocks);
					deob.deobfuscateMethodEnd(blocks);
					if (options.ControlFlowDeobfuscation)
						blocks.deobfuscateLeaveObfuscation();

					IList<Instruction> allInstructions;
					IList<ExceptionHandler> allExceptionHandlers;
					blocks.getCode(out allInstructions, out allExceptionHandlers);
					DotNetUtils.restoreBody(method, allInstructions, allExceptionHandlers);
				}

				removeNoInliningAttribute(method);

				Log.deIndent();
			}
		}

		void deobfuscateStrings(Blocks blocks) {
			switch (options.StringDecrypterType) {
			case DecrypterType.None:
				break;

			case DecrypterType.Static:
				deob.deobfuscateStrings(blocks);
				break;

			case DecrypterType.Delegate:
			case DecrypterType.Emulate:
				dynamicStringDecrypter.decrypt(blocks);
				break;

			default:
				throw new ApplicationException(string.Format("Invalid string decrypter type '{0}'", options.StringDecrypterType));
			}
		}

		void removeNoInliningAttribute(MethodDefinition method) {
			method.ImplAttributes = method.ImplAttributes & ~MethodImplAttributes.NoInlining;
		}

		public override string ToString() {
			if (options == null || options.Filename == null)
				return base.ToString();
			return options.Filename;
		}

		[Flags]
		enum SimpleDeobFlags {
			HasDeobfuscated = 0x1,
		}
		Dictionary<MethodDefinition, SimpleDeobFlags> simpleDeobfuscatorFlags = new Dictionary<MethodDefinition, SimpleDeobFlags>();
		bool check(MethodDefinition method, SimpleDeobFlags flag) {
			SimpleDeobFlags oldFlags;
			simpleDeobfuscatorFlags.TryGetValue(method, out oldFlags);
			simpleDeobfuscatorFlags[method] = oldFlags | flag;
			return (oldFlags & flag) == flag;
		}

		void deobfuscate(MethodDefinition method, string msg, Action<Blocks> handler) {
			if (savedMethodBodies != null)
				savedMethodBodies.save(method);

			Log.v("{0}: {1} ({2:X8})", msg, method, method.MetadataToken.ToUInt32());
			Log.indent();

			if (method.HasBody) {
				var blocks = new Blocks(method);

				handler(blocks);

				IList<Instruction> allInstructions;
				IList<ExceptionHandler> allExceptionHandlers;
				blocks.getCode(out allInstructions, out allExceptionHandlers);
				DotNetUtils.restoreBody(method, allInstructions, allExceptionHandlers);
			}

			Log.deIndent();
		}

		void ISimpleDeobfuscator.deobfuscate(MethodDefinition method) {
			if (check(method, SimpleDeobFlags.HasDeobfuscated))
				return;

			deobfuscate(method, "Deobfuscating control flow", (blocks) => blocks.deobfuscate());
		}

		void ISimpleDeobfuscator.decryptStrings(MethodDefinition method, IDeobfuscator theDeob) {
			deobfuscate(method, "Static string decryption", (blocks) => theDeob.deobfuscateStrings(blocks));
		}

		void IDeobfuscatedFile.createAssemblyFile(byte[] data, string assemblyName) {
			var baseDir = Utils.getDirName(options.NewFilename);
			var newName = Path.Combine(baseDir, assemblyName + ".dll");
			Log.n("Creating file {0}", newName);
			using (var writer = new BinaryWriter(new FileStream(newName, FileMode.Create))) {
				writer.Write(data);
			}
		}

		void IDeobfuscatedFile.stringDecryptersAdded() {
			updateDynamicStringDecrypter();
		}
	}
}
