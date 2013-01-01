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

using System.Collections.Generic;
using dnlib.DotNet;
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.DeepSea {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "DeepSea";
		public const string THE_TYPE = "ds";
		BoolOption inlineMethods;
		BoolOption removeInlinedMethods;
		BoolOption decryptResources;
		BoolOption dumpEmbeddedAssemblies;
		BoolOption restoreFields;
		BoolOption renameResourceKeys;
		BoolOption castDeobfuscation;

		public DeobfuscatorInfo()
			: base() {
			inlineMethods = new BoolOption(null, makeArgName("inline"), "Inline short methods", true);
			removeInlinedMethods = new BoolOption(null, makeArgName("remove-inlined"), "Remove inlined methods", true);
			decryptResources = new BoolOption(null, makeArgName("rsrc"), "Decrypt resources", true);
			dumpEmbeddedAssemblies = new BoolOption(null, makeArgName("embedded"), "Dump embedded assemblies", true);
			restoreFields = new BoolOption(null, makeArgName("fields"), "Restore fields", true);
			renameResourceKeys = new BoolOption(null, makeArgName("keys"), "Rename resource keys", true);
			castDeobfuscation = new BoolOption(null, makeArgName("casts"), "Deobfuscate casts", true);
		}

		public override string Name {
			get { return THE_NAME; }
		}

		public override string Type {
			get { return THE_TYPE; }
		}

		public override IDeobfuscator createDeobfuscator() {
			return new Deobfuscator(new Deobfuscator.Options {
				ValidNameRegex = validNameRegex.get(),
				InlineMethods = inlineMethods.get(),
				RemoveInlinedMethods = removeInlinedMethods.get(),
				DecryptResources = decryptResources.get(),
				DumpEmbeddedAssemblies = dumpEmbeddedAssemblies.get(),
				RestoreFields = restoreFields.get(),
				RenameResourceKeys = renameResourceKeys.get(),
				CastDeobfuscation = castDeobfuscation.get(),
			});
		}

		protected override IEnumerable<Option> getOptionsInternal() {
			return new List<Option>() {
				inlineMethods,
				removeInlinedMethods,
				decryptResources,
				dumpEmbeddedAssemblies,
				restoreFields,
				renameResourceKeys,
				castDeobfuscation,
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		string obfuscatorName = DeobfuscatorInfo.THE_NAME;
		bool startedDeobfuscating = false;

		StringDecrypter stringDecrypter;
		ResourceResolver resourceResolver;
		AssemblyResolver assemblyResolver;
		FieldsRestorer fieldsRestorer;
		ArrayBlockState arrayBlockState;

		internal class Options : OptionsBase {
			public bool InlineMethods { get; set; }
			public bool RemoveInlinedMethods { get; set; }
			public bool DecryptResources { get; set; }
			public bool DumpEmbeddedAssemblies { get; set; }
			public bool RestoreFields { get; set; }
			public bool RenameResourceKeys { get; set; }
			public bool CastDeobfuscation { get; set; }
		}

		public override string Type {
			get { return DeobfuscatorInfo.THE_TYPE; }
		}

		public override string TypeLong {
			get { return DeobfuscatorInfo.THE_NAME; }
		}

		public override string Name {
			get { return obfuscatorName; }
		}

		protected override bool CanInlineMethods {
			get { return startedDeobfuscating ? options.InlineMethods : true; }
		}

		public override IEnumerable<IBlocksDeobfuscator> BlocksDeobfuscators {
			get {
				var list = new List<IBlocksDeobfuscator>(getBlocksDeobfuscators());
				if (CanInlineMethods)
					list.Add(new DsMethodCallInliner(new CachedCflowDeobfuscator(getBlocksDeobfuscators())));
				return list;
			}
		}

		List<IBlocksDeobfuscator> getBlocksDeobfuscators() {
			var list = new List<IBlocksDeobfuscator>();
			if (arrayBlockState != null && arrayBlockState.Detected)
				list.Add(new ArrayBlockDeobfuscator(arrayBlockState));
			if (!startedDeobfuscating || options.CastDeobfuscation)
				list.Add(new CastDeobfuscator());
			return list;
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;

			if (options.RenameResourceKeys)
				this.RenamingOptions |= RenamingOptions.RenameResourceKeys;
			else
				this.RenamingOptions &= ~RenamingOptions.RenameResourceKeys;
		}

		protected override int detectInternal() {
			int val = 0;

			int sum = toInt32(stringDecrypter.Detected) +
					toInt32(resourceResolver.Detected) +
					toInt32(assemblyResolver.Detected);
			if (sum > 0)
				val += 100 + 10 * (sum - 1);

			return val;
		}

		protected override void scanForObfuscator() {
			staticStringInliner.UseUnknownArgs = true;
			arrayBlockState = new ArrayBlockState(module);
			arrayBlockState.init(DeobfuscatedFile);
			stringDecrypter = new StringDecrypter(module);
			stringDecrypter.find(DeobfuscatedFile);
			resourceResolver = new ResourceResolver(module, DeobfuscatedFile, this);
			resourceResolver.find();
			assemblyResolver = new AssemblyResolver(module, DeobfuscatedFile, this);
			assemblyResolver.find();
			obfuscatorName = detectVersion();
		}

		string detectVersion() {
			switch (stringDecrypter.Version) {
			case StringDecrypter.DecrypterVersion.V1_3:
				if (detectMethodProxyObfuscation())
					return DeobfuscatorInfo.THE_NAME + " 3.5";
				return DeobfuscatorInfo.THE_NAME + " 1.x-3.x";
			case StringDecrypter.DecrypterVersion.V4_0:
				return DeobfuscatorInfo.THE_NAME + " 4.0";
			case StringDecrypter.DecrypterVersion.V4_1:
				return DeobfuscatorInfo.THE_NAME + " 4.1";
			}

			return DeobfuscatorInfo.THE_NAME;
		}

		bool detectMethodProxyObfuscation() {
			const int MIN_FOUND_PROXIES = 10;

			int foundProxies = 0, checkedMethods = 0;
			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					if (foundProxies >= MIN_FOUND_PROXIES)
						goto done;
					if (!method.IsStatic || method.Body == null)
						continue;
					if (checkedMethods++ >= 1000)
						goto done;
					if (!DsMethodCallInliner.canInline(method))
						continue;
					foundProxies++;
				}
			}
done:
			return foundProxies >= MIN_FOUND_PROXIES;
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();

			if (options.RestoreFields) {
				fieldsRestorer = new FieldsRestorer(module);
				fieldsRestorer.initialize();
			}

			foreach (var method in stringDecrypter.DecrypterMethods) {
				staticStringInliner.add(method, (method2, gim, args) => {
					return stringDecrypter.decrypt(method2, args);
				});
			}
			DeobfuscatedFile.stringDecryptersAdded();

			resourceResolver.initialize();
			decryptResources();

			dumpEmbeddedAssemblies();

			startedDeobfuscating = true;
		}

		void decryptResources() {
			if (!options.DecryptResources)
				return;
			EmbeddedResource rsrc;
			if (!resourceResolver.mergeResources(out rsrc))
				return;
			addResourceToBeRemoved(rsrc, "Encrypted resources");
			addCctorInitCallToBeRemoved(resourceResolver.InitMethod);
			addCallToBeRemoved(module.EntryPoint, resourceResolver.InitMethod);
			addMethodToBeRemoved(resourceResolver.InitMethod, "Resource resolver init method");
			addMethodToBeRemoved(resourceResolver.InitMethod2, "Resource resolver init method #2");
			addMethodToBeRemoved(resourceResolver.HandlerMethod, "Resource resolver handler method");
			addMethodToBeRemoved(resourceResolver.GetDataMethod, "Resource resolver 'get resource data' method");
		}

		void dumpEmbeddedAssemblies() {
			if (!options.DumpEmbeddedAssemblies)
				return;
			foreach (var info in assemblyResolver.getAssemblyInfos()) {
				if (info.resource != null && info.resource == resourceResolver.Resource)
					continue;
				DeobfuscatedFile.createAssemblyFile(info.data, info.simpleName, info.extension);
				addResourceToBeRemoved(info.resource, string.Format("Embedded assembly: {0}", info.fullName));
			}
			addCctorInitCallToBeRemoved(assemblyResolver.InitMethod);
			addCallToBeRemoved(module.EntryPoint, assemblyResolver.InitMethod);
			addMethodToBeRemoved(assemblyResolver.InitMethod, "Assembly resolver init method");
			addMethodToBeRemoved(assemblyResolver.HandlerMethod, "Assembly resolver handler method");
			addMethodToBeRemoved(assemblyResolver.DecryptMethod, "Assembly resolver decrypt method");
		}

		public override void deobfuscateMethodEnd(Blocks blocks) {
			if (options.RestoreFields)
				fieldsRestorer.deobfuscate(blocks);
			base.deobfuscateMethodEnd(blocks);
		}

		public override void deobfuscateEnd() {
			if (options.RestoreFields && CanRemoveTypes)
				fieldsRestorer.cleanUp();
			removeInlinedMethods();

			if (options.RestoreFields)
				addTypesToBeRemoved(fieldsRestorer.FieldStructs, "Type with moved fields");

			if (CanRemoveStringDecrypterType) {
				addMethodsToBeRemoved(stringDecrypter.DecrypterMethods, "String decrypter method");
				stringDecrypter.cleanup();
			}

			addFieldsToBeRemoved(arrayBlockState.cleanUp(), "Control flow obfuscation array");

			base.deobfuscateEnd();
		}

		void removeInlinedMethods() {
			if (!options.InlineMethods || !options.RemoveInlinedMethods)
				return;
			removeInlinedMethods(DsInlinedMethodsFinder.find(module, staticStringInliner.Methods));
		}

		public override IEnumerable<int> getStringDecrypterMethods() {
			var list = new List<int>();
			foreach (var method in stringDecrypter.DecrypterMethods)
				list.Add(method.MDToken.ToInt32());
			return list;
		}
	}
}
