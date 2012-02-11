/*
    Copyright (C) 2011-2012 de4dot@gmail.com

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
using Mono.Cecil;
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

		public DeobfuscatorInfo()
			: base() {
			inlineMethods = new BoolOption(null, makeArgName("inline"), "Inline short methods", true);
			removeInlinedMethods = new BoolOption(null, makeArgName("remove-inlined"), "Remove inlined methods", true);
			decryptResources = new BoolOption(null, makeArgName("rsrc"), "Decrypt resources", true);
			dumpEmbeddedAssemblies = new BoolOption(null, makeArgName("embedded"), "Dump embedded assemblies", true);
			restoreFields = new BoolOption(null, makeArgName("fields"), "Restore fields", true);
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
			});
		}

		protected override IEnumerable<Option> getOptionsInternal() {
			return new List<Option>() {
				inlineMethods,
				removeInlinedMethods,
				decryptResources,
				dumpEmbeddedAssemblies,
				restoreFields,
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

		internal class Options : OptionsBase {
			public bool InlineMethods { get; set; }
			public bool RemoveInlinedMethods { get; set; }
			public bool DecryptResources { get; set; }
			public bool DumpEmbeddedAssemblies { get; set; }
			public bool RestoreFields { get; set; }
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

		public override IMethodCallInliner MethodCallInliner {
			get {
				if (CanInlineMethods)
					return new MethodCallInliner();
				return new NoMethodInliner();
			}
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
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
			case StringDecrypter.DecrypterVersion.V4:
				return DeobfuscatorInfo.THE_NAME + " 4.x";
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
					if (!DeepSea.MethodCallInliner.canInline(method))
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
				staticStringInliner.add(method, (method2, args) => {
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
			addMethodToBeRemoved(resourceResolver.HandlerMethod, "Resource resolver handler method");
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
		}

		public override void deobfuscateMethodEnd(Blocks blocks) {
			if (options.RestoreFields)
				fieldsRestorer.deobfuscate(blocks);
			base.deobfuscateMethodEnd(blocks);
		}

		public override void deobfuscateEnd() {
			if (options.RestoreFields)
				fieldsRestorer.cleanUp();
			removeInlinedMethods();

			if (options.RestoreFields)
				addTypesToBeRemoved(fieldsRestorer.FieldStructs, "Type with moved fields");

			if (Operations.DecryptStrings != OpDecryptString.None) {
				addMethodsToBeRemoved(stringDecrypter.DecrypterMethods, "String decrypter method");
				stringDecrypter.cleanup();
			}

			base.deobfuscateEnd();
		}

		void removeInlinedMethods() {
			if (!options.InlineMethods || !options.RemoveInlinedMethods)
				return;
			removeInlinedMethods(DsInlinedMethodsFinder.find(module));
		}

		public override IEnumerable<string> getStringDecrypterMethods() {
			var list = new List<string>();
			foreach (var method in stringDecrypter.DecrypterMethods)
				list.Add(method.MetadataToken.ToInt32().ToString("X8"));
			return list;
		}
	}
}
