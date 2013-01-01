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
using System.Globalization;
using System.IO;
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;
using dnlib.PE;
using de4dot.code.deobfuscators;
using de4dot.blocks;
using de4dot.blocks.cflow;
using de4dot.code.AssemblyClient;
using de4dot.code.renamer;

namespace de4dot.code {
	public class ObfuscatedFile : IObfuscatedFile, IDeobfuscatedFile {
		Options options;
		ModuleDefMD module;
		IDeobfuscator deob;
		IDeobfuscatorContext deobfuscatorContext;
		AssemblyModule assemblyModule;
		IAssemblyClient assemblyClient;
		DynamicStringInliner dynamicStringInliner;
		IAssemblyClientFactory assemblyClientFactory;
		SavedMethodBodies savedMethodBodies;
		bool userStringDecrypterMethods = false;

		class SavedMethodBodies {
			Dictionary<MethodDef, SavedMethodBody> savedMethodBodies = new Dictionary<MethodDef, SavedMethodBody>();

			class SavedMethodBody {
				MethodDef method;
				IList<Instruction> instructions;
				IList<ExceptionHandler> exceptionHandlers;

				public SavedMethodBody(MethodDef method) {
					this.method = method;
					DotNetUtils.copyBody(method, out instructions, out exceptionHandlers);
				}

				public void restore() {
					DotNetUtils.restoreBody(method, instructions, exceptionHandlers);
				}
			}

			public void save(MethodDef method) {
				if (isSaved(method))
					return;
				savedMethodBodies[method] = new SavedMethodBody(method);
			}

			public void restoreAll() {
				foreach (var smb in savedMethodBodies.Values)
					smb.restore();
				savedMethodBodies.Clear();
			}

			public bool isSaved(MethodDef method) {
				return savedMethodBodies.ContainsKey(method);
			}
		}

		public class Options {
			public string Filename { get; set; }
			public string NewFilename { get; set; }
			public string ForcedObfuscatorType { get; set; }
			public DecrypterType StringDecrypterType { get; set; }
			public List<string> StringDecrypterMethods { get; private set; }
			public bool ControlFlowDeobfuscation { get; set; }
			public bool KeepObfuscatorTypes { get; set; }
			public bool PreserveTokens { get; set; }
			public MetaDataFlags MetaDataFlags { get; set; }
			public RenamerFlags RenamerFlags { get; set; }

			public Options() {
				StringDecrypterType = DecrypterType.Default;
				StringDecrypterMethods = new List<string>();
			}
		}

		public string Filename {
			get { return options.Filename; }
		}

		public string NewFilename {
			get { return options.NewFilename; }
		}

		public ModuleDefMD ModuleDefMD {
			get { return module; }
		}

		public INameChecker NameChecker {
			get { return deob; }
		}

		public bool RenameResourcesInCode {
			get { return deob.TheOptions.RenameResourcesInCode; }
		}

		public bool RemoveNamespaceWithOneType {
			get { return (deob.RenamingOptions & RenamingOptions.RemoveNamespaceIfOneType) != 0; }
		}

		public bool RenameResourceKeys {
			get { return (deob.RenamingOptions & RenamingOptions.RenameResourceKeys) != 0; }
		}

		public IDeobfuscator Deobfuscator {
			get { return deob; }
		}

		public IDeobfuscatorContext DeobfuscatorContext {
			get { return deobfuscatorContext; }
			set { deobfuscatorContext = value; }
		}

		public ObfuscatedFile(Options options, ModuleContext moduleContext, IAssemblyClientFactory assemblyClientFactory) {
			this.assemblyClientFactory = assemblyClientFactory;
			this.options = options;
			userStringDecrypterMethods = options.StringDecrypterMethods.Count > 0;
			options.Filename = Utils.getFullPath(options.Filename);
			assemblyModule = new AssemblyModule(options.Filename, moduleContext);

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
			return noExt + "-cleaned" + ext;
		}

		public void load(IList<IDeobfuscator> deobfuscators) {
			try {
				loadModule(deobfuscators);
				TheAssemblyResolver.Instance.addSearchDirectory(Utils.getDirName(Filename));
				TheAssemblyResolver.Instance.addSearchDirectory(Utils.getDirName(NewFilename));
				detectObfuscator(deobfuscators);
				if (deob == null)
					throw new ApplicationException("Could not detect obfuscator!");
				initializeDeobfuscator();
			}
			finally {
				foreach (var d in deobfuscators) {
					if (d != deob && d != null)
						d.Dispose();
				}
			}
		}

		void loadModule(IEnumerable<IDeobfuscator> deobfuscators) {
			ModuleDefMD oldModule = module;
			try {
				module = assemblyModule.load();
			}
			catch (BadImageFormatException) {
				if (!unpackNativeImage(deobfuscators))
					throw new BadImageFormatException();
				Logger.v("Unpacked native file");
			}
			finally {
				if (oldModule != null)
					oldModule.Dispose();
			}
		}

		bool unpackNativeImage(IEnumerable<IDeobfuscator> deobfuscators) {
			using (var peImage = new PEImage(Filename)) {
				foreach (var deob in deobfuscators) {
					byte[] unpackedData = null;
					try {
						unpackedData = deob.unpackNativeFile(peImage);
					}
					catch {
					}
					if (unpackedData == null)
						continue;

					var oldModule = module;
					try {
						module = assemblyModule.load(unpackedData);
					}
					catch {
						Logger.w("Could not load unpacked data. File: {0}, deobfuscator: {0}", peImage.FileName ?? "(unknown filename)", deob.TypeLong);
						continue;
					}
					finally {
						if (oldModule != null)
							oldModule.Dispose();
					}
					this.deob = deob;
					return true;
				}
			}

			return false;
		}

		void initializeDeobfuscator() {
			if (options.StringDecrypterType == DecrypterType.Default)
				options.StringDecrypterType = deob.DefaultDecrypterType;
			if (options.StringDecrypterType == DecrypterType.Default)
				options.StringDecrypterType = DecrypterType.Static;

			deob.Operations = createOperations();
		}

		IOperations createOperations() {
			var op = new Operations();

			switch (options.StringDecrypterType) {
			case DecrypterType.None:
				op.DecryptStrings = OpDecryptString.None;
				break;
			case DecrypterType.Static:
				op.DecryptStrings = OpDecryptString.Static;
				break;
			default:
				op.DecryptStrings = OpDecryptString.Dynamic;
				break;
			}

			op.KeepObfuscatorTypes = options.KeepObfuscatorTypes;
			op.MetaDataFlags = options.MetaDataFlags;
			op.RenamerFlags = options.RenamerFlags;

			return op;
		}

		void detectObfuscator(IEnumerable<IDeobfuscator> deobfuscators) {

			// The deobfuscators may call methods to deobfuscate control flow and decrypt
			// strings (statically) in order to detect the obfuscator.
			if (!options.ControlFlowDeobfuscation || options.StringDecrypterType == DecrypterType.None)
				savedMethodBodies = new SavedMethodBodies();

			// It's not null if it unpacked a native file
			if (this.deob != null) {
				deob.init(module);
				deob.DeobfuscatedFile = this;
				deob.detect();
				return;
			}

			foreach (var deob in deobfuscators) {
				deob.init(module);
				deob.DeobfuscatedFile = this;
			}

			if (options.ForcedObfuscatorType != null) {
				foreach (var deob in deobfuscators) {
					if (string.Equals(options.ForcedObfuscatorType, deob.Type, StringComparison.OrdinalIgnoreCase)) {
						this.deob = deob;
						deob.detect();
						return;
					}
				}
			}
			else
				this.deob = detectObfuscator2(deobfuscators);
		}

		IDeobfuscator detectObfuscator2(IEnumerable<IDeobfuscator> deobfuscators) {
			var allDetected = new List<IDeobfuscator>();
			IDeobfuscator detected = null;
			int detectVal = 0;
			foreach (var deob in deobfuscators) {
				this.deob = deob;	// So we can call deob.CanInlineMethods in deobfuscate()
				int val;
				try {
					val = deob.detect();
				}
				catch {
					val = deob.Type == "un" ? 1 : 0;
				}
				Logger.v("{0,3}: {1}", val, deob.TypeLong);
				if (val > 0 && deob.Type != "un")
					allDetected.Add(deob);
				if (val > detectVal) {
					detectVal = val;
					detected = deob;
				}
			}
			this.deob = null;

			if (allDetected.Count > 1) {
				Logger.n("More than one obfuscator detected:");
				Logger.Instance.indent();
				foreach (var deob in allDetected)
					Logger.n("{0} (use: -p {1})", deob.Name, deob.Type);
				Logger.Instance.deIndent();
			}

			return detected;
		}

		MetaDataFlags getMetaDataFlags() {
			var mdFlags = options.MetaDataFlags | deob.MetaDataFlags;

			// Always preserve tokens if it's an unknown obfuscator
			if (deob.Type == "un") {
				mdFlags |= MetaDataFlags.PreserveRids |
						MetaDataFlags.PreserveUSOffsets |
						MetaDataFlags.PreserveBlobOffsets |
						MetaDataFlags.PreserveExtraSignatureData;
			}

			return mdFlags;
		}

		public void save() {
			Logger.n("Saving {0}", options.NewFilename);
			var mdFlags = getMetaDataFlags();
			if (!options.ControlFlowDeobfuscation)
				mdFlags |= MetaDataFlags.KeepOldMaxStack;
			assemblyModule.save(options.NewFilename, mdFlags, deob as IModuleWriterListener);
		}

		IList<MethodDef> getAllMethods() {
			var list = new List<MethodDef>();

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
				var newProcFactory = assemblyClientFactory as NewProcessAssemblyClientFactory;
				if (newProcFactory != null)
					assemblyClient = newProcFactory.create(module);
				else
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
			throw new UserException(string.Format("Deobfuscator {0} does not support this string decryption type", deob.TypeLong));
		}

		public void deobfuscate() {
			Logger.n("Cleaning {0}", options.Filename);
			initAssemblyClient();

			for (int i = 0; ; i++) {
				byte[] fileData = null;
				DumpedMethods dumpedMethods = null;
				if (!deob.getDecryptedModule(i, ref fileData, ref dumpedMethods))
					break;
				reloadModule(fileData, dumpedMethods);
			}

			deob.deobfuscateBegin();
			deobfuscateMethods();
			deob.deobfuscateEnd();
		}

		void reloadModule(byte[] newModuleData, DumpedMethods dumpedMethods) {
			Logger.v("Reloading decrypted assembly (original filename: {0})", Filename);
			simpleDeobfuscatorFlags.Clear();
			using (var oldModule = module) {
				module = assemblyModule.reload(newModuleData, createDumpedMethodsRestorer(dumpedMethods), deob as IStringDecrypter);
				deob = deob.moduleReloaded(module);
			}
			initializeDeobfuscator();
			deob.DeobfuscatedFile = this;
			updateDynamicStringInliner();
		}

		DumpedMethodsRestorer createDumpedMethodsRestorer(DumpedMethods dumpedMethods) {
			if (dumpedMethods == null || dumpedMethods.Count == 0)
				return null;
			return new DumpedMethodsRestorer(dumpedMethods);
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

			dynamicStringInliner = new DynamicStringInliner(assemblyClient);
			updateDynamicStringInliner();
		}

		void updateDynamicStringInliner() {
			if (dynamicStringInliner != null)
				dynamicStringInliner.init(getMethodTokens());
		}

		IEnumerable<int> getMethodTokens() {
			if (!userStringDecrypterMethods)
				return deob.getStringDecrypterMethods();

			var tokens = new List<int>();

			foreach (var val in options.StringDecrypterMethods) {
				var tokenStr = val.Trim();
				if (Utils.StartsWith(tokenStr, "0x", StringComparison.OrdinalIgnoreCase))
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
					if (!method.IsStatic)
						continue;
					if (method.MethodSig.GetRetType().GetElementType() != ElementType.String && method.MethodSig.GetRetType().GetElementType() != ElementType.Object)
						continue;
					if (methodName != null && methodName != method.Name)
						continue;

					var sig = method.MethodSig;
					if (argsStrings == null) {
						if (sig.Params.Count == 0)
							continue;
					}
					else {
						if (argsStrings.Length != sig.Params.Count)
							continue;
						for (int i = 0; i < argsStrings.Length; i++) {
							if (argsStrings[i] != sig.Params[i].FullName)
								continue;
						}
					}

					Logger.v("Adding string decrypter; token: {0:X8}, method: {1}", method.MDToken.ToInt32(), Utils.removeNewlines(method.FullName));
					tokens.Add(method.MDToken.ToInt32());
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

			if (Utils.StartsWith(remaining, "(", StringComparison.Ordinal)) {
				stringArgs = remaining;
			}
			else if (remaining.Length > 0)
				throw new UserException(string.Format("Invalid method desc: '{0}'", methodDesc));

			if (stringArgs != null) {
				if (Utils.StartsWith(stringArgs, "(", StringComparison.Ordinal))
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

			if (!options.ControlFlowDeobfuscation) {
				if (options.KeepObfuscatorTypes || deob.Type == "un")
					return;
			}

			bool isVerbose = !Logger.Instance.IgnoresEvent(LoggerEvent.Verbose);
			bool isVV = !Logger.Instance.IgnoresEvent(LoggerEvent.VeryVerbose);
			if (isVerbose)
				Logger.v("Deobfuscating methods");
			var methodPrinter = new MethodPrinter();
			var cflowDeobfuscator = new BlocksCflowDeobfuscator(deob.BlocksDeobfuscators);
			foreach (var method in getAllMethods()) {
				if (isVerbose) {
					Logger.v("Deobfuscating {0} ({1:X8})", Utils.removeNewlines(method), method.MDToken.ToUInt32());
					Logger.Instance.indent();
				}

				int oldIndentLevel = Logger.Instance.IndentLevel;
				try {
					deobfuscate(method, cflowDeobfuscator, methodPrinter, isVerbose, isVV);
				}
				catch (ApplicationException) {
					throw;
				}
				catch (Exception ex) {
					if (!canLoadMethodBody(method)) {
						if (isVerbose)
							Logger.v("Invalid method body. {0:X8}", method.MDToken.ToInt32());
						method.Body = new CilBody();
					}
					else {
						Logger.w("Could not deobfuscate method {0:X8}. Hello, E.T.: {1}",	// E.T. = exception type
								method.MDToken.ToInt32(),
								ex.GetType());
					}
				}
				finally {
					Logger.Instance.IndentLevel = oldIndentLevel;
				}
				removeNoInliningAttribute(method);

				if (isVerbose)
					Logger.Instance.deIndent();
			}
		}

		static bool canLoadMethodBody(MethodDef method) {
			try {
				var body = method.Body;
				return true;
			}
			catch {
				return false;
			}
		}

		bool CanOptimizeLocals() {
			// Don't remove any locals if we must preserve StandAloneSig table
			return (getMetaDataFlags() & MetaDataFlags.PreserveStandAloneSigRids) == 0;
		}

		void deobfuscate(MethodDef method, BlocksCflowDeobfuscator cflowDeobfuscator, MethodPrinter methodPrinter, bool isVerbose, bool isVV) {
			if (!hasNonEmptyBody(method))
				return;

			var blocks = new Blocks(method);
			int numRemovedLocals = 0;
			int oldNumInstructions = method.Body.Instructions.Count;

			deob.deobfuscateMethodBegin(blocks);
			if (options.ControlFlowDeobfuscation) {
				cflowDeobfuscator.init(blocks);
				cflowDeobfuscator.deobfuscate();
			}

			if (deob.deobfuscateOther(blocks) && options.ControlFlowDeobfuscation)
				cflowDeobfuscator.deobfuscate();

			if (options.ControlFlowDeobfuscation) {
				if (CanOptimizeLocals())
					numRemovedLocals = blocks.optimizeLocals();
				blocks.repartitionBlocks();
			}

			deobfuscateStrings(blocks);
			deob.deobfuscateMethodEnd(blocks);

			IList<Instruction> allInstructions;
			IList<ExceptionHandler> allExceptionHandlers;
			blocks.getCode(out allInstructions, out allExceptionHandlers);
			DotNetUtils.restoreBody(method, allInstructions, allExceptionHandlers);

			if (isVerbose && numRemovedLocals > 0)
				Logger.v("Removed {0} unused local(s)", numRemovedLocals);
			int numRemovedInstructions = oldNumInstructions - method.Body.Instructions.Count;
			if (isVerbose && numRemovedInstructions > 0)
				Logger.v("Removed {0} dead instruction(s)", numRemovedInstructions);

			if (isVV) {
				Logger.log(LoggerEvent.VeryVerbose, "Deobfuscated code:");
				Logger.Instance.indent();
				methodPrinter.print(LoggerEvent.VeryVerbose, allInstructions, allExceptionHandlers);
				Logger.Instance.deIndent();
			}
		}

		bool hasNonEmptyBody(MethodDef method) {
			return method.HasBody && method.Body.Instructions.Count > 0;
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
				dynamicStringInliner.decrypt(blocks);
				break;

			default:
				throw new ApplicationException(string.Format("Invalid string decrypter type '{0}'", options.StringDecrypterType));
			}
		}

		void removeNoInliningAttribute(MethodDef method) {
			method.IsNoInlining = false;
			for (int i = 0; i < method.CustomAttributes.Count; i++) {
				var cattr = method.CustomAttributes[i];
				if (cattr.TypeFullName != "System.Runtime.CompilerServices.MethodImplAttribute")
					continue;
				int options = 0;
				if (!getMethodImplOptions(cattr, ref options))
					continue;
				if (options != 0 && options != (int)MethodImplAttributes.NoInlining)
					continue;
				method.CustomAttributes.RemoveAt(i);
				i--;
			}
		}

		static bool getMethodImplOptions(CustomAttribute cattr, ref int value) {
			if (cattr.IsRawBlob)
				return false;
			if (cattr.ConstructorArguments.Count != 1)
				return false;
			if (cattr.ConstructorArguments[0].Type.ElementType != ElementType.I2 &&
				cattr.ConstructorArguments[0].Type.FullName != "System.Runtime.CompilerServices.MethodImplOptions")
				return false;

			var arg = cattr.ConstructorArguments[0].Value;
			if (arg is short) {
				value = (short)arg;
				return true;
			}
			if (arg is int) {
				value = (int)arg;
				return true;
			}

			return false;
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
		Dictionary<MethodDef, SimpleDeobFlags> simpleDeobfuscatorFlags = new Dictionary<MethodDef, SimpleDeobFlags>();
		bool check(MethodDef method, SimpleDeobFlags flag) {
			SimpleDeobFlags oldFlags;
			simpleDeobfuscatorFlags.TryGetValue(method, out oldFlags);
			simpleDeobfuscatorFlags[method] = oldFlags | flag;
			return (oldFlags & flag) == flag;
		}

		void deobfuscate(MethodDef method, string msg, Action<Blocks> handler) {
			if (savedMethodBodies != null)
				savedMethodBodies.save(method);

			Logger.v("{0}: {1} ({2:X8})", msg, Utils.removeNewlines(method), method.MDToken.ToUInt32());
			Logger.Instance.indent();

			if (hasNonEmptyBody(method)) {
				try {
					var blocks = new Blocks(method);

					handler(blocks);

					IList<Instruction> allInstructions;
					IList<ExceptionHandler> allExceptionHandlers;
					blocks.getCode(out allInstructions, out allExceptionHandlers);
					DotNetUtils.restoreBody(method, allInstructions, allExceptionHandlers);
				}
				catch {
					Logger.v("Could not deobfuscate {0:X8}", method.MDToken.ToInt32());
				}
			}

			Logger.Instance.deIndent();
		}

		void ISimpleDeobfuscator.deobfuscate(MethodDef method) {
			((ISimpleDeobfuscator)this).deobfuscate(method, false);
		}

		void ISimpleDeobfuscator.deobfuscate(MethodDef method, bool force) {
			if (!force && check(method, SimpleDeobFlags.HasDeobfuscated))
				return;

			deobfuscate(method, "Deobfuscating control flow", (blocks) => {
				var cflowDeobfuscator = new BlocksCflowDeobfuscator(deob.BlocksDeobfuscators);
				cflowDeobfuscator.init(blocks);
				cflowDeobfuscator.deobfuscate();
			});
		}

		void ISimpleDeobfuscator.decryptStrings(MethodDef method, IDeobfuscator theDeob) {
			deobfuscate(method, "Static string decryption", (blocks) => theDeob.deobfuscateStrings(blocks));
		}

		void IDeobfuscatedFile.createAssemblyFile(byte[] data, string assemblyName, string extension) {
			if (extension == null)
				extension = ".dll";
			var baseDir = Utils.getDirName(options.NewFilename);
			var newName = Path.Combine(baseDir, assemblyName + extension);
			Logger.n("Creating file {0}", newName);
			File.WriteAllBytes(newName, data);
		}

		void IDeobfuscatedFile.stringDecryptersAdded() {
			updateDynamicStringInliner();
		}

		void IDeobfuscatedFile.setDeobfuscator(IDeobfuscator deob) {
			this.deob = deob;
		}

		public void Dispose() {
			deobfuscateCleanUp();
			if (module != null)
				module.Dispose();
			if (deob != null)
				deob.Dispose();
			module = null;
			deob = null;
		}
	}
}
