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
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;
using de4dot.renamer.asmmodules;

namespace de4dot.renamer {
	class MemberInfo {
		Ref memberRef;
		public string oldFullName;
		public string oldName;
		public string newName;
		public bool renamed;

		public MemberInfo(Ref memberRef) {
			this.memberRef = memberRef;
			oldFullName = memberRef.memberReference.FullName;
			oldName = memberRef.memberReference.Name;
			newName = memberRef.memberReference.Name;
		}

		public void rename(string newTypeName) {
			renamed = true;
			newName = newTypeName;
		}

		public bool gotNewName() {
			return oldName != newName;
		}
	}

	class TypeInfo : MemberInfo {
		public string oldNamespace;
		public string newNamespace;

		public TypeInfo(TypeDef type)
			: base(type) {
			oldNamespace = type.TypeDefinition.Namespace;
		}
	}

	class GenericParamInfo : MemberInfo {
		public GenericParamInfo(GenericParamDef genericParamDef)
			: base(genericParamDef) {
		}
	}

	class Renamer {
		public bool RenameNamespaces { get; set; }
		public bool RenameTypes { get; set; }
		public bool RenameProperties { get; set; }
		public bool RenameEvents { get; set; }
		public bool RenameFields { get; set; }
		public bool RenameGenericParams { get; set; }
		public bool RenameMethodArgs { get; set; }
		Modules modules = new Modules();
		DerivedFrom isWinFormsClass;
		Dictionary<TypeDef, TypeInfo> allTypeInfos = new Dictionary<TypeDef, TypeInfo>();
		Dictionary<GenericParamDef, GenericParamInfo> allGenericParamInfos = new Dictionary<GenericParamDef, GenericParamInfo>();
		Dictionary<MethodDef, string> suggestedMethodNames = new Dictionary<MethodDef, string>();

		static string[] WINFORMS_CLASSES = new string[] {
#region Win Forms class names
			"System.Windows.Forms.Control",
			"System.Windows.Forms.AxHost",
			"System.Windows.Forms.ButtonBase",
			"System.Windows.Forms.Button",
			"System.Windows.Forms.CheckBox",
			"System.Windows.Forms.RadioButton",
			"System.Windows.Forms.DataGrid",
			"System.Windows.Forms.DataGridView",
			"System.Windows.Forms.DataVisualization.Charting.Chart",
			"System.Windows.Forms.DateTimePicker",
			"System.Windows.Forms.GroupBox",
			"System.Windows.Forms.Integration.ElementHost",
			"System.Windows.Forms.Label",
			"System.Windows.Forms.LinkLabel",
			"System.Windows.Forms.ListControl",
			"System.Windows.Forms.ComboBox",
			"Microsoft.VisualBasic.Compatibility.VB6.DriveListBox",
			"System.Windows.Forms.DataGridViewComboBoxEditingControl",
			"System.Windows.Forms.ListBox",
			"Microsoft.VisualBasic.Compatibility.VB6.DirListBox",
			"Microsoft.VisualBasic.Compatibility.VB6.FileListBox",
			"System.Windows.Forms.CheckedListBox",
			"System.Windows.Forms.ListView",
			"System.Windows.Forms.MdiClient",
			"System.Windows.Forms.MonthCalendar",
			"System.Windows.Forms.PictureBox",
			"System.Windows.Forms.PrintPreviewControl",
			"System.Windows.Forms.ProgressBar",
			"System.Windows.Forms.ScrollableControl",
			"System.Windows.Forms.ContainerControl",
			"System.Windows.Forms.Form",
			"System.ComponentModel.Design.CollectionEditor.CollectionForm",
			"System.Messaging.Design.QueuePathDialog",
			"System.ServiceProcess.Design.ServiceInstallerDialog",
			"System.Web.UI.Design.WebControls.CalendarAutoFormatDialog",
			"System.Web.UI.Design.WebControls.RegexEditorDialog",
			"System.Windows.Forms.Design.ComponentEditorForm",
			"System.Windows.Forms.PrintPreviewDialog",
			"System.Windows.Forms.ThreadExceptionDialog",
			"System.Workflow.Activities.Rules.Design.RuleConditionDialog",
			"System.Workflow.Activities.Rules.Design.RuleSetDialog",
			"System.Workflow.ComponentModel.Design.ThemeConfigurationDialog",
			"System.Workflow.ComponentModel.Design.TypeBrowserDialog",
			"System.Workflow.ComponentModel.Design.WorkflowPageSetupDialog",
			"System.Windows.Forms.PropertyGrid",
			"System.Windows.Forms.SplitContainer",
			"System.Windows.Forms.ToolStripContainer",
			"System.Windows.Forms.ToolStripPanel",
			"System.Windows.Forms.UpDownBase",
			"System.Windows.Forms.DomainUpDown",
			"System.Windows.Forms.NumericUpDown",
			"System.Windows.Forms.UserControl",
			"Microsoft.VisualBasic.Compatibility.VB6.ADODC",
			"System.Web.UI.Design.WebControls.ParameterEditorUserControl",
			"System.Workflow.ComponentModel.Design.WorkflowOutline",
			"System.Workflow.ComponentModel.Design.WorkflowView",
			"System.Windows.Forms.Design.ComponentTray",
			"System.Windows.Forms.Panel",
			"System.Windows.Forms.Design.ComponentEditorPage",
			"System.Windows.Forms.FlowLayoutPanel",
			"System.Windows.Forms.SplitterPanel",
			"System.Windows.Forms.TableLayoutPanel",
			"System.ComponentModel.Design.ByteViewer",
			"System.Windows.Forms.TabPage",
			"System.Windows.Forms.ToolStripContentPanel",
			"System.Windows.Forms.ToolStrip",
			"System.Windows.Forms.BindingNavigator",
			"System.Windows.Forms.MenuStrip",
			"System.Windows.Forms.StatusStrip",
			"System.Windows.Forms.ToolStripDropDown",
			"System.Windows.Forms.ToolStripDropDownMenu",
			"System.Windows.Forms.ContextMenuStrip",
			"System.Windows.Forms.ToolStripOverflow",
			"System.Windows.Forms.ScrollBar",
			"System.Windows.Forms.HScrollBar",
			"System.Windows.Forms.VScrollBar",
			"System.Windows.Forms.Splitter",
			"System.Windows.Forms.StatusBar",
			"System.Windows.Forms.TabControl",
			"System.Windows.Forms.TextBoxBase",
			"System.Windows.Forms.MaskedTextBox",
			"System.Windows.Forms.RichTextBox",
			"System.Windows.Forms.TextBox",
			"System.Windows.Forms.DataGridTextBox",
			"System.Windows.Forms.DataGridViewTextBoxEditingControl",
			"System.Windows.Forms.ToolBar",
			"System.Windows.Forms.TrackBar",
			"System.Windows.Forms.TreeView",
			"System.ComponentModel.Design.ObjectSelectorEditor.Selector",
			"System.Windows.Forms.WebBrowserBase",
			"System.Windows.Forms.WebBrowser",
#endregion
		};

		public Renamer(IEnumerable<IObfuscatedFile> files) {
			RenameNamespaces = true;
			RenameTypes = true;
			RenameProperties = true;
			RenameEvents = true;
			RenameFields = true;
			RenameGenericParams = true;
			RenameMethodArgs = true;

			foreach (var file in files)
				modules.add(new Module(file));

			isWinFormsClass = new DerivedFrom(WINFORMS_CLASSES);
		}

		public void rename() {
			if (modules.Empty)
				return;
			Log.n("Renaming all obfuscated symbols");

			modules.initialize();
			modules.initializeVirtualMembers();
			renameTypeDefinitions();
			modules.cleanUp();
		}

		void renameTypeDefinitions() {
			Log.v("Renaming obfuscated type definitions");

			prepareRenameTypes();
		}

		void prepareRenameTypes() {
			foreach (var type in modules.AllTypes)
				allTypeInfos[type] = new TypeInfo(type);

			var state = new TypeRenamerState();
			prepareRenameTypes(modules.BaseTypes, state);
			fixClsTypeNames();
			renameTypeDefinitions(modules.NonNestedTypes);
		}

		void renameTypeDefinitions(IEnumerable<TypeDef> typeDefs) {
			Log.indent();
			foreach (var typeDef in typeDefs) {
				rename(typeDef);
				renameTypeDefinitions(typeDef.NestedTypes);
			}
			Log.deIndent();
		}

		void rename(TypeDef type) {
			var typeDefinition = type.TypeDefinition;
			var info = allTypeInfos[type];

			Log.v("Type: {0} ({1:X8})", typeDefinition.FullName, typeDefinition.MetadataToken.ToUInt32());
			Log.indent();

			renameGenericParams(type.GenericParams);

			if (RenameTypes && info.gotNewName()) {
				var old = typeDefinition.Name;
				typeDefinition.Name = info.newName;
				Log.v("Name: {0} => {1}", old, typeDefinition.Name);
			}

			if (RenameNamespaces && info.newNamespace != null) {
				var old = typeDefinition.Namespace;
				typeDefinition.Namespace = info.newNamespace;
				Log.v("Namespace: {0} => {1}", old, typeDefinition.Namespace);
			}

			Log.deIndent();
		}

		void renameGenericParams(IEnumerable<GenericParamDef> genericParams) {
			if (!RenameGenericParams)
				return;
			foreach (var param in genericParams) {
				var info = allGenericParamInfos[param];
				if (!info.gotNewName())
					continue;
				param.GenericParameter.Name = info.newName;
				Log.v("GenParam: {0} => {1}", info.oldFullName, param.GenericParameter.FullName);
			}
		}

		// Make sure the renamed types are using valid CLS names. That means renaming all
		// generic types from eg. Class1 to Class1`2. If we don't do this, some decompilers
		// (eg. ILSpy v1.0) won't produce correct output.
		void fixClsTypeNames() {
			foreach (var type in modules.NonNestedTypes)
				fixClsTypeNames(null, type);
		}

		void fixClsTypeNames(TypeDef nesting, TypeDef nested) {
			int nestingCount = nesting == null ? 0 : nesting.GenericParams.Count;
			int arity = nested.GenericParams.Count - nestingCount;
			var nestedInfo = allTypeInfos[nested];
			if (nestedInfo.renamed && arity > 0)
				nestedInfo.newName += "`" + arity;
			foreach (var nestedType in nested.NestedTypes)
				fixClsTypeNames(nested, nestedType);
		}

		void prepareRenameTypes(IEnumerable<TypeDef> types, TypeRenamerState state) {
			foreach (var typeDef in types) {
				prepareRenameTypes(typeDef, state);
				prepareRenameTypes(typeDef.derivedTypes, state);
			}
		}

		void prepareRenameTypes(TypeDef type, TypeRenamerState state) {
			var info = allTypeInfos[type];
			var checker = type.Module.ObfuscatedFile.NameChecker;

			if (RenameNamespaces) {
				if (info.newNamespace == null && info.oldNamespace != "") {
					if (!checker.isValidNamespaceName(info.oldNamespace)) {
						info.newNamespace = state.createNamespace(info.oldNamespace);
					}
				}
			}

			if (RenameTypes) {
				if (info.oldFullName != "<Module>" && !checker.isValidTypeName(info.oldName)) {
					string origClassName = null;
					if (isWinFormsClass.check(type))
						origClassName = findWindowsFormsClassName(type);
					if (origClassName != null && checker.isValidTypeName(origClassName))
						info.rename(state.getTypeName(info.oldName, origClassName));
					else {
						ITypeNameCreator nameCreator = type.isGlobalType() ?
												state.globalTypeNameCreator :
												state.internalTypeNameCreator;
						string newBaseType = null;
						TypeInfo baseInfo;
						if (type.baseType != null && allTypeInfos.TryGetValue(type.baseType.typeDef, out baseInfo)) {
							if (baseInfo.renamed)
								newBaseType = baseInfo.newName;
						}
						info.rename(nameCreator.create(type.TypeDefinition, newBaseType));
					}
				}
			}

			if (RenameGenericParams)
				prepareRenameGenericParams(type.GenericParams, checker);
		}

		void prepareRenameGenericParams(IEnumerable<GenericParamDef> genericParams, INameChecker checker, IEnumerable<GenericParamDef> otherGenericParams = null) {
			var usedNames = new Dictionary<string, bool>(StringComparer.Ordinal);
			var nameCreator = new GenericParamNameCreator();

			foreach (var gp in genericParams)
				allGenericParamInfos[gp] = new GenericParamInfo(gp);

			if (otherGenericParams != null) {
				foreach (var param in otherGenericParams) {
					var gpInfo = allGenericParamInfos[param];
					usedNames[gpInfo.newName] = true;
				}
			}

			foreach (var param in genericParams) {
				var gpInfo = allGenericParamInfos[param];
				if (!checker.isValidGenericParamName(gpInfo.oldName) || usedNames.ContainsKey(gpInfo.oldName)) {
					string newName;
					do {
						newName = nameCreator.create();
					} while (usedNames.ContainsKey(newName));
					usedNames[newName] = true;
					gpInfo.rename(newName);
				}
			}
		}

		string findWindowsFormsClassName(TypeDef type) {
			foreach (var methodDef in type.getAllMethods()) {
				if (methodDef.MethodDefinition.Body == null)
					continue;
				if (methodDef.MethodDefinition.IsStatic || methodDef.MethodDefinition.IsVirtual)
					continue;
				var instructions = methodDef.MethodDefinition.Body.Instructions;
				for (int i = 2; i < instructions.Count; i++) {
					var call = instructions[i];
					if (call.OpCode.Code != Code.Call && call.OpCode.Code != Code.Callvirt)
						continue;
					if (!isWindowsFormsSetNameMethod(call.Operand as MethodReference))
						continue;

					var ldstr = instructions[i - 1];
					if (ldstr.OpCode.Code != Code.Ldstr)
						continue;
					var className = ldstr.Operand as string;
					if (className == null)
						continue;

					if (DotNetUtils.getArgIndex(methodDef.MethodDefinition, instructions[i - 2]) != 0)
						continue;

					findInitializeComponentMethod(type, methodDef);
					return className;
				}
			}
			return null;
		}

		void findInitializeComponentMethod(TypeDef type, MethodDef possibleInitMethod) {
			foreach (var methodDef in type.getAllMethods()) {
				if (methodDef.MethodDefinition.Name != ".ctor")
					continue;
				if (methodDef.MethodDefinition.Body == null)
					continue;
				foreach (var instr in methodDef.MethodDefinition.Body.Instructions) {
					if (instr.OpCode.Code != Code.Call && instr.OpCode.Code != Code.Callvirt)
						continue;
					if (!MemberReferenceHelper.compareMethodReferenceAndDeclaringType(possibleInitMethod.MethodDefinition, instr.Operand as MethodReference))
						continue;

					suggestedMethodNames[possibleInitMethod] = "InitializeComponent";
					return;
				}
			}
		}

		static bool isWindowsFormsSetNameMethod(MethodReference method) {
			if (method == null)
				return false;
			if (method.Name != "set_Name")
				return false;
			if (method.MethodReturnType.ReturnType.FullName != "System.Void")
				return false;
			if (method.Parameters.Count != 1)
				return false;
			if (method.Parameters[0].ParameterType.FullName != "System.String")
				return false;
			if (!Utils.StartsWith(method.DeclaringType.FullName, "System.Windows.Forms.", StringComparison.Ordinal))
				return false;
			return true;
		}
	}
}
