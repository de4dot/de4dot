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
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;
using de4dot.deobfuscators;

namespace de4dot.old_renamer {
	abstract class Ref {
		public string NewName { get; set; }
		public string OldName { get; private set; }
		public string OldFullName { get; private set; }
		public int Index { get; private set; }
		public MemberReference MemberReference { get; private set; }
		public TypeDef Owner { get; set; }
		public bool Renamed { get; set; }

		public Ref(MemberReference mr, TypeDef owner, int index) {
			MemberReference = mr;
			NewName = OldName = mr.Name;
			OldFullName = mr.FullName;
			Owner = owner;
			Index = index;
		}

		public bool gotNewName() {
			return NewName != OldName;
		}

		public abstract bool isSame(MemberReference mr);

		public bool rename(string newName) {
			if (Renamed)
				return false;
			Renamed = true;
			NewName = newName;
			return true;
		}

		static protected bool isVirtual(MethodDefinition m) {
			return m != null && m.IsVirtual;
		}

		protected static IList<GenericParamDef> createGenericParamDefList(IEnumerable<GenericParameter> parameters) {
			var list = new List<GenericParamDef>();
			if (parameters == null)
				return list;
			int i = 0;
			foreach (var param in parameters)
				list.Add(new GenericParamDef(param, i++));
			return list;
		}

		public override string ToString() {
			return MemberReference != null ? MemberReference.ToString() : null;
		}
	}

	class FieldDef : Ref {
		public FieldDef(FieldDefinition fieldDefinition, TypeDef owner, int index)
			: base(fieldDefinition, owner, index) {
		}

		public FieldDefinition FieldDefinition {
			get { return (FieldDefinition)MemberReference; }
		}

		public override bool isSame(MemberReference mr) {
			return MemberReferenceHelper.compareFieldReference(FieldDefinition, mr as FieldReference);
		}
	}

	class EventRef : Ref {
		public EventRef(EventReference eventReference, TypeDef owner, int index)
			: base(eventReference, owner, index) {
		}

		public EventReference EventReference {
			get { return (EventReference)MemberReference; }
		}

		public override bool isSame(MemberReference mr) {
			return MemberReferenceHelper.compareEventReference(EventReference, mr as EventReference);
		}
	}

	class EventDef : EventRef {
		public EventDef(EventDefinition eventDefinition, TypeDef owner, int index)
			: base(eventDefinition, owner, index) {
		}

		public EventDefinition EventDefinition {
			get { return (EventDefinition)MemberReference; }
		}

		public IEnumerable<MethodDefinition> methodDefinitions() {
			if (EventDefinition.AddMethod != null)
				yield return EventDefinition.AddMethod;
			if (EventDefinition.RemoveMethod != null)
				yield return EventDefinition.RemoveMethod;
			if (EventDefinition.InvokeMethod != null)
				yield return EventDefinition.InvokeMethod;
			if (EventDefinition.OtherMethods != null) {
				foreach (var m in EventDefinition.OtherMethods)
					yield return m;
			}
		}

		// Returns one of the overridden methods or null if none found
		public MethodReference getOverrideMethod() {
			foreach (var method in methodDefinitions()) {
				if (method.HasOverrides)
					return method.Overrides[0];
			}
			return null;
		}

		public bool isVirtual() {
			foreach (var method in methodDefinitions()) {
				if (isVirtual(method))
					return true;
			}
			return false;
		}
	}

	class PropertyRef : Ref {
		public PropertyRef(PropertyReference propertyReference, TypeDef owner, int index)
			: base(propertyReference, owner, index) {
		}

		public PropertyReference PropertyReference {
			get { return (PropertyReference)MemberReference; }
		}

		public override bool isSame(MemberReference mr) {
			return MemberReferenceHelper.comparePropertyReference(PropertyReference, mr as PropertyReference);
		}
	}

	class PropertyDef : PropertyRef {
		public PropertyDef(PropertyDefinition propertyDefinition, TypeDef owner, int index)
			: base(propertyDefinition, owner, index) {
		}

		public PropertyDefinition PropertyDefinition {
			get { return (PropertyDefinition)MemberReference; }
		}

		public IEnumerable<MethodDefinition> methodDefinitions() {
			if (PropertyDefinition.GetMethod != null)
				yield return PropertyDefinition.GetMethod;
			if (PropertyDefinition.SetMethod != null)
				yield return PropertyDefinition.SetMethod;
			if (PropertyDefinition.OtherMethods != null) {
				foreach (var m in PropertyDefinition.OtherMethods)
					yield return m;
			}
		}

		// Returns one of the overridden methods or null if none found
		public MethodReference getOverrideMethod() {
			foreach (var method in methodDefinitions()) {
				if (method.HasOverrides)
					return method.Overrides[0];
			}
			return null;
		}

		public bool isVirtual() {
			foreach (var method in methodDefinitions()) {
				if (isVirtual(method))
					return true;
			}
			return false;
		}
	}

	class MethodRef : Ref {
		public IList<ParamDef> paramDefs = new List<ParamDef>();

		public IList<ParamDef> ParamDefs {
			get { return paramDefs; }
		}

		public MethodRef(MethodReference methodReference, TypeDef owner, int index)
			: base(methodReference, owner, index) {
			if (methodReference.HasParameters) {
				for (int i = 0; i < methodReference.Parameters.Count; i++) {
					var param = methodReference.Parameters[i];
					paramDefs.Add(new ParamDef(param, i));
				}
			}
		}

		public MethodReference MethodReference {
			get { return (MethodReference)MemberReference; }
		}

		public override bool isSame(MemberReference mr) {
			return MemberReferenceHelper.compareMethodReference(MethodReference, mr as MethodReference);
		}
	}

	class MethodDef : MethodRef {
		IList<GenericParamDef> genericParams;

		public IList<GenericParamDef> GenericParams {
			get { return genericParams; }
		}
		public PropertyDef Property { get; set; }
		public EventDef Event { get; set; }

		public MethodDef(MethodDefinition methodDefinition, TypeDef owner, int index)
			: base(methodDefinition, owner, index) {
			genericParams = createGenericParamDefList(MethodDefinition.GenericParameters);
		}

		public MethodDefinition MethodDefinition {
			get { return (MethodDefinition)MemberReference; }
		}

		public bool isVirtual() {
			return isVirtual(MethodDefinition);
		}
	}

	class ParamDef {
		public ParameterDefinition ParameterDefinition { get; set; }
		public string OldName { get; private set; }
		public string NewName { get; set; }
		public int Index { get; private set; }
		public bool Renamed { get; set; }

		public ParamDef(ParameterDefinition parameterDefinition, int index) {
			this.ParameterDefinition = parameterDefinition;
			NewName = OldName = parameterDefinition.Name;
			Index = index;
		}

		public bool gotNewName() {
			return NewName != OldName;
		}
	}

	class GenericParamDef : Ref {
		public GenericParamDef(GenericParameter genericParameter, int index)
			: base(genericParameter, null, index) {
		}

		public GenericParameter GenericParameter {
			get { return (GenericParameter)MemberReference; }
		}

		public override bool isSame(MemberReference mr) {
			throw new NotImplementedException();
		}
	}

	class TypeInfo {
		public TypeReference typeReference;
		public TypeDef typeDef;
		public TypeInfo(TypeReference typeReference, TypeDef typeDef) {
			this.typeReference = typeReference;
			this.typeDef = typeDef;
		}
	}

	class TypeDef : Ref {
		static Dictionary<string, bool> windowsFormsControlClasses = new Dictionary<string, bool>(StringComparer.Ordinal);
		static TypeDef() {
			windowsFormsControlClasses["System.Windows.Forms.Control"] = true;
			windowsFormsControlClasses["System.Windows.Forms.AxHost"] = true;
			windowsFormsControlClasses["System.Windows.Forms.ButtonBase"] = true;
			windowsFormsControlClasses["System.Windows.Forms.Button"] = true;
			windowsFormsControlClasses["System.Windows.Forms.CheckBox"] = true;
			windowsFormsControlClasses["System.Windows.Forms.RadioButton"] = true;
			windowsFormsControlClasses["System.Windows.Forms.DataGrid"] = true;
			windowsFormsControlClasses["System.Windows.Forms.DataGridView"] = true;
			windowsFormsControlClasses["System.Windows.Forms.DataVisualization.Charting.Chart"] = true;
			windowsFormsControlClasses["System.Windows.Forms.DateTimePicker"] = true;
			windowsFormsControlClasses["System.Windows.Forms.GroupBox"] = true;
			windowsFormsControlClasses["System.Windows.Forms.Integration.ElementHost"] = true;
			windowsFormsControlClasses["System.Windows.Forms.Label"] = true;
			windowsFormsControlClasses["System.Windows.Forms.LinkLabel"] = true;
			windowsFormsControlClasses["System.Windows.Forms.ListControl"] = true;
			windowsFormsControlClasses["System.Windows.Forms.ComboBox"] = true;
			windowsFormsControlClasses["Microsoft.VisualBasic.Compatibility.VB6.DriveListBox"] = true;
			windowsFormsControlClasses["System.Windows.Forms.DataGridViewComboBoxEditingControl"] = true;
			windowsFormsControlClasses["System.Windows.Forms.ListBox"] = true;
			windowsFormsControlClasses["Microsoft.VisualBasic.Compatibility.VB6.DirListBox"] = true;
			windowsFormsControlClasses["Microsoft.VisualBasic.Compatibility.VB6.FileListBox"] = true;
			windowsFormsControlClasses["System.Windows.Forms.CheckedListBox"] = true;
			windowsFormsControlClasses["System.Windows.Forms.ListView"] = true;
			windowsFormsControlClasses["System.Windows.Forms.MdiClient"] = true;
			windowsFormsControlClasses["System.Windows.Forms.MonthCalendar"] = true;
			windowsFormsControlClasses["System.Windows.Forms.PictureBox"] = true;
			windowsFormsControlClasses["System.Windows.Forms.PrintPreviewControl"] = true;
			windowsFormsControlClasses["System.Windows.Forms.ProgressBar"] = true;
			windowsFormsControlClasses["System.Windows.Forms.ScrollableControl"] = true;
			windowsFormsControlClasses["System.Windows.Forms.ContainerControl"] = true;
			windowsFormsControlClasses["System.Windows.Forms.Form"] = true;
			windowsFormsControlClasses["System.ComponentModel.Design.CollectionEditor.CollectionForm"] = true;
			windowsFormsControlClasses["System.Messaging.Design.QueuePathDialog"] = true;
			windowsFormsControlClasses["System.ServiceProcess.Design.ServiceInstallerDialog"] = true;
			windowsFormsControlClasses["System.Web.UI.Design.WebControls.CalendarAutoFormatDialog"] = true;
			windowsFormsControlClasses["System.Web.UI.Design.WebControls.RegexEditorDialog"] = true;
			windowsFormsControlClasses["System.Windows.Forms.Design.ComponentEditorForm"] = true;
			windowsFormsControlClasses["System.Windows.Forms.PrintPreviewDialog"] = true;
			windowsFormsControlClasses["System.Windows.Forms.ThreadExceptionDialog"] = true;
			windowsFormsControlClasses["System.Workflow.Activities.Rules.Design.RuleConditionDialog"] = true;
			windowsFormsControlClasses["System.Workflow.Activities.Rules.Design.RuleSetDialog"] = true;
			windowsFormsControlClasses["System.Workflow.ComponentModel.Design.ThemeConfigurationDialog"] = true;
			windowsFormsControlClasses["System.Workflow.ComponentModel.Design.TypeBrowserDialog"] = true;
			windowsFormsControlClasses["System.Workflow.ComponentModel.Design.WorkflowPageSetupDialog"] = true;
			windowsFormsControlClasses["System.Windows.Forms.PropertyGrid"] = true;
			windowsFormsControlClasses["System.Windows.Forms.SplitContainer"] = true;
			windowsFormsControlClasses["System.Windows.Forms.ToolStripContainer"] = true;
			windowsFormsControlClasses["System.Windows.Forms.ToolStripPanel"] = true;
			windowsFormsControlClasses["System.Windows.Forms.UpDownBase"] = true;
			windowsFormsControlClasses["System.Windows.Forms.DomainUpDown"] = true;
			windowsFormsControlClasses["System.Windows.Forms.NumericUpDown"] = true;
			windowsFormsControlClasses["System.Windows.Forms.UserControl"] = true;
			windowsFormsControlClasses["Microsoft.VisualBasic.Compatibility.VB6.ADODC"] = true;
			windowsFormsControlClasses["System.Web.UI.Design.WebControls.ParameterEditorUserControl"] = true;
			windowsFormsControlClasses["System.Workflow.ComponentModel.Design.WorkflowOutline"] = true;
			windowsFormsControlClasses["System.Workflow.ComponentModel.Design.WorkflowView"] = true;
			windowsFormsControlClasses["System.Windows.Forms.Design.ComponentTray"] = true;
			windowsFormsControlClasses["System.Windows.Forms.Panel"] = true;
			windowsFormsControlClasses["System.Windows.Forms.Design.ComponentEditorPage"] = true;
			windowsFormsControlClasses["System.Windows.Forms.FlowLayoutPanel"] = true;
			windowsFormsControlClasses["System.Windows.Forms.SplitterPanel"] = true;
			windowsFormsControlClasses["System.Windows.Forms.TableLayoutPanel"] = true;
			windowsFormsControlClasses["System.ComponentModel.Design.ByteViewer"] = true;
			windowsFormsControlClasses["System.Windows.Forms.TabPage"] = true;
			windowsFormsControlClasses["System.Windows.Forms.ToolStripContentPanel"] = true;
			windowsFormsControlClasses["System.Windows.Forms.ToolStrip"] = true;
			windowsFormsControlClasses["System.Windows.Forms.BindingNavigator"] = true;
			windowsFormsControlClasses["System.Windows.Forms.MenuStrip"] = true;
			windowsFormsControlClasses["System.Windows.Forms.StatusStrip"] = true;
			windowsFormsControlClasses["System.Windows.Forms.ToolStripDropDown"] = true;
			windowsFormsControlClasses["System.Windows.Forms.ToolStripDropDownMenu"] = true;
			windowsFormsControlClasses["System.Windows.Forms.ContextMenuStrip"] = true;
			windowsFormsControlClasses["System.Windows.Forms.ToolStripOverflow"] = true;
			windowsFormsControlClasses["System.Windows.Forms.ScrollBar"] = true;
			windowsFormsControlClasses["System.Windows.Forms.HScrollBar"] = true;
			windowsFormsControlClasses["System.Windows.Forms.VScrollBar"] = true;
			windowsFormsControlClasses["System.Windows.Forms.Splitter"] = true;
			windowsFormsControlClasses["System.Windows.Forms.StatusBar"] = true;
			windowsFormsControlClasses["System.Windows.Forms.TabControl"] = true;
			windowsFormsControlClasses["System.Windows.Forms.TextBoxBase"] = true;
			windowsFormsControlClasses["System.Windows.Forms.MaskedTextBox"] = true;
			windowsFormsControlClasses["System.Windows.Forms.RichTextBox"] = true;
			windowsFormsControlClasses["System.Windows.Forms.TextBox"] = true;
			windowsFormsControlClasses["System.Windows.Forms.DataGridTextBox"] = true;
			windowsFormsControlClasses["System.Windows.Forms.DataGridViewTextBoxEditingControl"] = true;
			windowsFormsControlClasses["System.Windows.Forms.ToolBar"] = true;
			windowsFormsControlClasses["System.Windows.Forms.TrackBar"] = true;
			windowsFormsControlClasses["System.Windows.Forms.TreeView"] = true;
			windowsFormsControlClasses["System.ComponentModel.Design.ObjectSelectorEditor.Selector"] = true;
			windowsFormsControlClasses["System.Windows.Forms.WebBrowserBase"] = true;
			windowsFormsControlClasses["System.Windows.Forms.WebBrowser"] = true;
		}

		Dictionary<MethodDef, string> newMethodNames = new Dictionary<MethodDef, string>();
		Dictionary<PropertyDef, string> newPropertyNames = new Dictionary<PropertyDef, string>();

		public IDefFinder defFinder;
		public TypeInfo baseType = null;
		public IList<TypeInfo> interfaces = new List<TypeInfo>();	// directly implemented interfaces
		public IList<TypeDef> derivedTypes = new List<TypeDef>();
		public Module module;
		string newNamespace = null;

		EventDefDict events = new EventDefDict();
		FieldDefDict fields = new FieldDefDict();
		MethodDefDict methods = new MethodDefDict();
		PropertyDefDict properties = new PropertyDefDict();
		TypeDefDict types = new TypeDefDict();
		IList<GenericParamDef> genericParams;
		public TypeDefinition TypeDefinition {
			get { return (TypeDefinition)MemberReference; }
		}
		public MemberRenameState MemberRenameState { get; set; }
		public MemberRenameState InterfaceScopeState { get; set; }
		bool prepareRenameMembersCalled = false;

		public IEnumerable<TypeDef> NestedTypes {
			get { return types.getSorted(); }
		}

		public TypeDef NestingType { get; set; }

		public IList<GenericParamDef> GenericParams {
			get { return genericParams; }
		}

		public bool IsRenamable {
			get { return module != null; }
		}

		bool IsDelegate { get; set; }

		public string NewNamespace {
			get { return newNamespace; }
			set { newNamespace = value; }
		}

		public TypeDef(TypeDefinition typeDefinition)
			: this(typeDefinition, null) {
		}

		public TypeDef(TypeDefinition typeDefinition, Module module, int index = 0)
			: base(typeDefinition, null, index) {
			this.module = module;
			genericParams = createGenericParamDefList(TypeDefinition.GenericParameters);
		}

		public override bool isSame(MemberReference mr) {
			return MemberReferenceHelper.compareTypes(TypeDefinition, mr as TypeReference);
		}

		public bool isInterface() {
			return TypeDefinition.IsInterface;
		}

		public IEnumerable<MethodDef> Methods {
			get { return methods.getAll(); }
		}

		bool? isWindowsFormsControlDerivedClass_cached;
		bool isWindowsFormsControlDerivedClass() {
			if (!isWindowsFormsControlDerivedClass_cached.HasValue)
				isWindowsFormsControlDerivedClass_cached = isWindowsFormsControlDerivedClassInternal();
			return isWindowsFormsControlDerivedClass_cached.Value;
		}

		bool isWindowsFormsControlDerivedClassInternal() {
			if (windowsFormsControlClasses.ContainsKey(OldFullName))
				return true;
			if (baseType != null)
				return baseType.typeDef.isWindowsFormsControlDerivedClass();
			if (TypeDefinition.BaseType != null)
				return windowsFormsControlClasses.ContainsKey(TypeDefinition.BaseType.FullName);
			return false;
		}

		public void addMembers() {
			var type = TypeDefinition;

			for (int i = 0; i < type.Events.Count; i++)
				add(new EventDef(type.Events[i], this, i));
			for (int i = 0; i < type.Fields.Count; i++)
				add(new FieldDef(type.Fields[i], this, i));
			for (int i = 0; i < type.Methods.Count; i++)
				add(new MethodDef(type.Methods[i], this, i));
			for (int i = 0; i < type.Properties.Count; i++)
				add(new PropertyDef(type.Properties[i], this, i));

			foreach (var propDef in properties.getAll()) {
				foreach (var method in propDef.methodDefinitions()) {
					var methodDef = find(method);
					if (methodDef == null)
						throw new ApplicationException("Could not find property method");
					methodDef.Property = propDef;
				}
			}

			foreach (var eventDef in events.getAll()) {
				foreach (var method in eventDef.methodDefinitions()) {
					var methodDef = find(method);
					if (methodDef == null)
						throw new ApplicationException("Could not find event method");
					methodDef.Event = eventDef;
				}
			}
		}

		public void addInterface(TypeDef ifaceDef, TypeReference iface) {
			if (ifaceDef == null || iface == null)
				return;
			interfaces.Add(new TypeInfo(iface, ifaceDef));
		}

		public void addBaseType(TypeDef baseDef, TypeReference baseRef) {
			if (baseDef == null || baseRef == null)
				return;
			baseType = new TypeInfo(baseRef, baseDef);
			IsDelegate = baseRef.FullName == "System.Delegate" || baseRef.FullName == "System.MulticastDelegate";
		}

		// Called when all types have been renamed
		public void onTypesRenamed() {
			events.onTypesRenamed();
			fields.onTypesRenamed();
			methods.onTypesRenamed();
			types.onTypesRenamed();
		}

		public IEnumerable<TypeDef> getAllInterfaces() {
			if (isInterface())
				yield return this;
			foreach (var ifaceInfo in interfaces) {
				foreach (var iface in ifaceInfo.typeDef.getAllInterfaces())
					yield return iface;
			}
			foreach (var typeDef in derivedTypes) {
				foreach (var iface in typeDef.getAllInterfaces())
					yield return iface;
			}
		}

		public IEnumerable<TypeDef> getAllRenamableInterfaces() {
			foreach (var iface in getAllInterfaces()) {
				if (iface.IsRenamable)
					yield return iface;
			}
		}

		public void add(EventDef e) {
			events.add(e);
		}

		public void add(FieldDef f) {
			fields.add(f);
		}

		public void add(MethodDef m) {
			methods.add(m);
		}

		public void add(PropertyDef p) {
			properties.add(p);
		}

		public void add(TypeDef t) {
			types.add(t);
		}

		public MethodDef find(MethodReference mr) {
			return methods.find(mr);
		}

		public FieldDef find(FieldReference fr) {
			return fields.find(fr);
		}

		IEnumerable<FieldDef> getInstanceFields() {
			foreach (var fieldDef in fields.getSorted()) {
				if (!fieldDef.FieldDefinition.IsStatic)
					yield return fieldDef;
			}
		}

		bool isNested() {
			return NestingType != null;
		}

		bool isGlobalType() {
			if (!isNested())
				return TypeDefinition.IsPublic;
			var mask = TypeDefinition.Attributes & TypeAttributes.VisibilityMask;
			switch (mask) {
			case TypeAttributes.NestedPrivate:
			case TypeAttributes.NestedAssembly:
			case TypeAttributes.NestedFamANDAssem:
				return false;
			case TypeAttributes.NestedPublic:
			case TypeAttributes.NestedFamily:
			case TypeAttributes.NestedFamORAssem:
				return NestingType.isGlobalType();
			default:
				return false;
			}
		}

		// Renames name, namespace, and generic parameters if needed. Does not rename members.
		public void prepareRename(TypeNameState typeNameState) {
			var typeDefinition = TypeDefinition;
			ITypeNameCreator nameCreator = isGlobalType() ?
					typeNameState.globalTypeNameCreator :
					typeNameState.internalTypeNameCreator;

			if (OldFullName != "<Module>" && !typeNameState.IsValidName(OldName)) {
				var newBaseType = baseType != null && baseType.typeDef.Renamed ? baseType.typeDef.NewName : null;
				string origClassName = null;
				if (isWindowsFormsControlDerivedClass())
					origClassName = findWindowsFormsClassName();
				if (origClassName != null && typeNameState.IsValidName(origClassName))
					rename(typeNameState.currentNames.newName(OldName, new NameCreator2(origClassName)));
				else
					rename(nameCreator.newName(typeDefinition, newBaseType));
			}

			if (newNamespace == null && typeDefinition.Namespace != "" && !typeNameState.isValidNamespace(typeDefinition.Namespace))
				newNamespace = typeNameState.newNamespace(typeDefinition.Namespace);

			prepareRenameGenericParams(genericParams, typeNameState.IsValidName);
		}

		string findWindowsFormsClassName() {
			foreach (var methodDef in methods.getAll()) {
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

					findInitializeComponentMethod(methodDef);
					return className;
				}
			}
			return null;
		}

		void findInitializeComponentMethod(MethodDef possibleInitMethod) {
			foreach (var methodDef in methods.getAll()) {
				if (methodDef.OldName != ".ctor")
					continue;
				if (methodDef.MethodDefinition.Body == null)
					continue;
				foreach (var instr in methodDef.MethodDefinition.Body.Instructions) {
					if (instr.OpCode.Code != Code.Call && instr.OpCode.Code != Code.Callvirt)
						continue;
					if (!MemberReferenceHelper.compareMethodReferenceAndDeclaringType(possibleInitMethod.MethodDefinition, instr.Operand as MethodReference))
						continue;

					newMethodNames[possibleInitMethod] = "InitializeComponent";
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

		public void rename() {
			var typeDefinition = TypeDefinition;

			Log.v("Type: {0} ({1:X8})", TypeDefinition.FullName, TypeDefinition.MetadataToken.ToUInt32());
			Log.indent();

			renameGenericParams(genericParams);

			if (gotNewName()) {
				var old = typeDefinition.Name;
				typeDefinition.Name = NewName;
				Log.v("Name: {0} => {1}", old, typeDefinition.Name);
			}

			if (newNamespace != null) {
				var old = typeDefinition.Namespace;
				typeDefinition.Namespace = newNamespace;
				Log.v("Namespace: {0} => {1}", old, typeDefinition.Namespace);
			}

			Log.deIndent();
		}

		static void prepareRenameGenericParams(IList<GenericParamDef> genericParams, Func<string, bool> isValidName, IList<GenericParamDef> otherGenericParams = null) {
			Dictionary<string, bool> usedNames = new Dictionary<string, bool>(StringComparer.Ordinal);
			INameCreator nameCreator = new GenericParamNameCreator();

			if (otherGenericParams != null) {
				foreach (var param in otherGenericParams)
					usedNames[param.NewName] = true;
			}

			foreach (var param in genericParams) {
				if (!isValidName(param.OldName) || usedNames.ContainsKey(param.OldName)) {
					string newName;
					do {
						newName = nameCreator.newName();
					} while (usedNames.ContainsKey(newName));
					usedNames[newName] = true;
					param.rename(newName);
				}
			}
		}

		static void renameGenericParams(IList<GenericParamDef> genericParams) {
			foreach (var param in genericParams) {
				if (!param.gotNewName())
					continue;
				param.GenericParameter.Name = param.NewName;
				Log.v("GenParam: {0} => {1}", param.OldFullName, param.GenericParameter.FullName);
			}
		}

		public void renameMembers() {
			Log.v("Type: {0}", TypeDefinition.FullName);
			Log.indent();

			renameFields();
			renameProperties();
			renameEvents();
			renameMethods();

			Log.deIndent();
		}

		void renameFields() {
			foreach (var fieldDef in fields.getSorted()) {
				if (!fieldDef.gotNewName())
					continue;
				fieldDef.FieldDefinition.Name = fieldDef.NewName;
				Log.v("Field: {0} ({1:X8}) => {2}", fieldDef.OldFullName, fieldDef.FieldDefinition.MetadataToken.ToUInt32(), fieldDef.FieldDefinition.FullName);
			}
		}

		void renameProperties() {
			foreach (var propDef in properties.getSorted()) {
				if (!propDef.gotNewName())
					continue;
				propDef.PropertyDefinition.Name = propDef.NewName;
				Log.v("Property: {0} ({1:X8}) => {2}", propDef.OldFullName, propDef.PropertyDefinition.MetadataToken.ToUInt32(), propDef.PropertyDefinition.FullName);
			}
		}

		void renameEvents() {
			foreach (var eventDef in events.getSorted()) {
				if (!eventDef.gotNewName())
					continue;
				eventDef.EventDefinition.Name = eventDef.NewName;
				Log.v("Event: {0} ({1:X8}) => {2}", eventDef.OldFullName, eventDef.EventDefinition.MetadataToken.ToUInt32(), eventDef.EventDefinition.FullName);
			}
		}

		void renameMethods() {
			foreach (var methodDef in methods.getSorted()) {
				Log.v("Method {0} ({1:X8})", methodDef.OldFullName, methodDef.MethodDefinition.MetadataToken.ToUInt32());
				Log.indent();

				renameGenericParams(methodDef.GenericParams);

				if (methodDef.gotNewName()) {
					methodDef.MethodReference.Name = methodDef.NewName;
					Log.v("Name: {0} => {1}", methodDef.OldFullName, methodDef.MethodReference.FullName);
				}

				foreach (var param in methodDef.ParamDefs) {
					if (!param.gotNewName())
						continue;
					param.ParameterDefinition.Name = param.NewName;
					Log.v("Param ({0}/{1}): {2} => {3}", param.Index + 1, methodDef.ParamDefs.Count, param.OldName, param.NewName);
				}

				Log.deIndent();
			}
		}

		public void initializeVirtualMembers() {
			expandGenerics();
			foreach (var propDef in properties.getSorted()) {
				if (propDef.isVirtual())
					MemberRenameState.add(propDef);
			}
			foreach (var eventDef in events.getSorted()) {
				if (eventDef.isVirtual())
					MemberRenameState.add(eventDef);
			}
			foreach (var methodDef in methods.getSorted()) {
				if (methodDef.isVirtual())
					MemberRenameState.add(methodDef);
			}
		}

		public void prepareRenameMembers() {
			if (prepareRenameMembersCalled)
				return;
			prepareRenameMembersCalled = true;

			foreach (var ifaceInfo in interfaces)
				ifaceInfo.typeDef.prepareRenameMembers();
			if (baseType != null)
				baseType.typeDef.prepareRenameMembers();

			if (MemberRenameState == null)
				MemberRenameState = baseType.typeDef.MemberRenameState.clone();

			if (IsRenamable) {
				foreach (var fieldDef in fields.getAll())
					MemberRenameState.variableNameState.addFieldName(fieldDef.OldName);
				foreach (var methodDef in methods.getAll())
					MemberRenameState.variableNameState.addMethodName(methodDef.OldName);
			}

			// For each base type and interface it implements, add all its virtual methods, props,
			// and events if the type is a non-renamable type (eg. it's from mscorlib or some other
			// non-deobfuscated assembly).
			if (IsRenamable) {
				foreach (var ifaceInfo in interfaces) {
					if (!ifaceInfo.typeDef.IsRenamable)
						MemberRenameState.mergeRenamed(ifaceInfo.typeDef.MemberRenameState);
				}
				if (baseType != null && !baseType.typeDef.IsRenamable)
					MemberRenameState.mergeRenamed(baseType.typeDef.MemberRenameState);
			}

			if (InterfaceScopeState != null)
				MemberRenameState.mergeRenamed(InterfaceScopeState);

			expandGenerics();

			if (IsRenamable) {
				MemberRenameState.variableNameState.IsValidName = module.IsValidName;

				if (isWindowsFormsControlDerivedClass())
					initializeWindowsFormsFieldsAndProps();

				prepareRenameFields();		// must be first
				prepareRenameProperties();
				prepareRenameEvents();

				initializeEventHandlerNames();

				prepareRenameMethods();		// must be last
			}
		}

		// Replaces the generic params with the generic args, if any
		void expandGenerics() {
			foreach (var typeInfo in getTypeInfos()) {
				var git = typeInfo.typeReference as GenericInstanceType;
				if (git == null)
					continue;

				if (git.GenericArguments.Count != typeInfo.typeDef.TypeDefinition.GenericParameters.Count) {
					throw new ApplicationException(string.Format("# args ({0}) != # params ({1})",
							git.GenericArguments.Count,
							typeInfo.typeDef.TypeDefinition.GenericParameters.Count));
				}
				expandProperties(git);
				expandEvents(git);
				expandMethods(git);
			}
		}

		IEnumerable<TypeInfo> getTypeInfos() {
			if (baseType != null)
				yield return baseType;
			foreach (var typeInfo in interfaces)
				yield return typeInfo;
		}

		void expandProperties(GenericInstanceType git) {
			foreach (var propRef in new List<PropertyRef>(MemberRenameState.properties.Values)) {
				var newPropRef = new GenericPropertyRefExpander(propRef, git).expand();
				if (ReferenceEquals(newPropRef, propRef))
					continue;
				MemberRenameState.add(newPropRef);
			}
		}

		void expandEvents(GenericInstanceType git) {
			foreach (var eventRef in new List<EventRef>(MemberRenameState.events.Values)) {
				var newEventRef = new GenericEventRefExpander(eventRef, git).expand();
				if (ReferenceEquals(eventRef, newEventRef))
					continue;
				MemberRenameState.add(newEventRef);
			}
		}

		void expandMethods(GenericInstanceType git) {
			foreach (var methodRef in new List<MethodRef>(MemberRenameState.methods.Values)) {
				var newMethodRef = new GenericMethodRefExpander(methodRef, git).expand();
				if (ReferenceEquals(methodRef, newMethodRef))
					continue;
				MemberRenameState.add(newMethodRef);
			}
		}

		bool hasFlagsAttribute() {
			if (TypeDefinition.CustomAttributes != null) {
				foreach (var attr in TypeDefinition.CustomAttributes) {
					if (MemberReferenceHelper.verifyType(attr.AttributeType, "mscorlib", "System.FlagsAttribute"))
						return true;
				}
			}
			return false;
		}

		void prepareRenameFields() {
			var variableNameState = MemberRenameState.variableNameState;

			if (TypeDefinition.IsEnum) {
				var instanceFields = new List<FieldDef>(getInstanceFields());
				if (instanceFields.Count == 1) {
					var fieldDef = instanceFields[0];
					if (fieldDef.rename("value__")) {
						fieldDef.FieldDefinition.IsRuntimeSpecialName = true;
						fieldDef.FieldDefinition.IsSpecialName = true;
					}
				}

				int i = 0;
				string nameFormat = hasFlagsAttribute() ? "flag_{0}" : "const_{0}";
				foreach (var fieldDef in fields.getSorted()) {
					if (fieldDef.Renamed)
						continue;
					if (!fieldDef.FieldDefinition.IsStatic || !fieldDef.FieldDefinition.IsLiteral)
						continue;
					if (!variableNameState.IsValidName(fieldDef.OldName))
						fieldDef.rename(string.Format(nameFormat, i));
					i++;
				}
			}
			foreach (var fieldDef in fields.getSorted()) {
				if (fieldDef.Renamed)
					continue;
				if (!variableNameState.IsValidName(fieldDef.OldName))
					fieldDef.rename(variableNameState.getNewFieldName(fieldDef.FieldDefinition));
			}
		}

		void initializeWindowsFormsFieldsAndProps() {
			var ourFields = new Dictionary<FieldReferenceAndDeclaringTypeKey, FieldDef>();
			foreach (var fieldDef in fields.getAll())
				ourFields[new FieldReferenceAndDeclaringTypeKey(fieldDef.FieldDefinition)] = fieldDef;
			var ourMethods = new Dictionary<MethodReferenceAndDeclaringTypeKey, MethodDef>();
			foreach (var methodDef in methods.getAll())
				ourMethods[new MethodReferenceAndDeclaringTypeKey(methodDef.MethodDefinition)] = methodDef;

			var variableNameState = MemberRenameState.variableNameState;
			foreach (var methodDef in methods.getAll()) {
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
					var fieldName = ldstr.Operand as string;
					if (fieldName == null || !variableNameState.IsValidName(fieldName))
						continue;
					if (!variableNameState.IsValidName(fieldName))
						continue;

					var instr = instructions[i - 2];
					FieldReference fieldRef = null;
					if (instr.OpCode.Code == Code.Call || instr.OpCode.Code == Code.Callvirt) {
						var calledMethod = instr.Operand as MethodReference;
						if (calledMethod == null)
							continue;
						MethodDef calledMethodDef;
						if (!ourMethods.TryGetValue(new MethodReferenceAndDeclaringTypeKey(calledMethod), out calledMethodDef))
							continue;
						fieldRef = getFieldReference(calledMethodDef.MethodDefinition);
						if (fieldRef == null)
							continue;

						var propDef = calledMethodDef.Property;
						if (propDef == null)
							continue;

						newPropertyNames[propDef] = fieldName;
						fieldName = "_" + fieldName;
					}
					else if (instr.OpCode.Code == Code.Ldfld) {
						fieldRef = instr.Operand as FieldReference;
					}

					if (fieldRef == null)
						continue;
					FieldDef fieldDef;
					if (!ourFields.TryGetValue(new FieldReferenceAndDeclaringTypeKey(fieldRef), out fieldDef))
						continue;

					if (fieldDef.Renamed)
						continue;

					fieldDef.rename(variableNameState.getNewFieldName(fieldDef.OldName, new NameCreator2(fieldName)));
				}
			}
		}

		static FieldReference getFieldReference(MethodDefinition method) {
			if (method == null || method.Body == null)
				return null;
			var instructions = method.Body.Instructions;
			int index = 0;
			var ldarg0 = DotNetUtils.getInstruction(instructions, ref index);
			if (ldarg0 == null || DotNetUtils.getArgIndex(method, ldarg0) != 0)
				return null;
			var ldfld = DotNetUtils.getInstruction(instructions, ref index);
			if (ldfld == null || ldfld.OpCode.Code != Code.Ldfld)
				return null;
			var ret = DotNetUtils.getInstruction(instructions, ref index);
			if (ret == null || ret.OpCode.Code != Code.Ret)
				return null;
			return ldfld.Operand as FieldReference;
		}

		void initializeEventHandlerNames() {
			var ourFields = new Dictionary<FieldReferenceAndDeclaringTypeKey, FieldDef>();
			foreach (var fieldDef in fields.getAll())
				ourFields[new FieldReferenceAndDeclaringTypeKey(fieldDef.FieldDefinition)] = fieldDef;
			var ourMethods = new Dictionary<MethodReferenceAndDeclaringTypeKey, MethodDef>();
			foreach (var methodDef in methods.getAll())
				ourMethods[new MethodReferenceAndDeclaringTypeKey(methodDef.MethodDefinition)] = methodDef;

			initVbEventHandlers(ourFields, ourMethods);
			initFieldEventHandlers(ourFields, ourMethods);
			initTypeEventHandlers(ourFields, ourMethods);
		}

		// VB initializes the handlers in the property setter, where it first removes the handler
		// from the previous control, and then adds the handler to the new control.
		void initVbEventHandlers(Dictionary<FieldReferenceAndDeclaringTypeKey, FieldDef> ourFields, Dictionary<MethodReferenceAndDeclaringTypeKey, MethodDef> ourMethods) {
			var variableNameState = MemberRenameState.variableNameState;
			foreach (var propDef in properties.getAll()) {
				var setter = propDef.PropertyDefinition.SetMethod;
				if (setter == null)
					continue;
				var setterDef = find(setter);
				if (setterDef == null)
					continue;

				string eventName;
				var handler = getVbHandler(setterDef.MethodDefinition, out eventName);
				if (handler == null)
					continue;
				MethodDef handlerDef;
				if (!ourMethods.TryGetValue(new MethodReferenceAndDeclaringTypeKey(handler), out handlerDef))
					continue;

				if (!MemberRenameState.variableNameState.IsValidName(eventName))
					continue;

				newMethodNames[handlerDef] = string.Format("{0}_{1}", propDef.NewName, eventName);
			}
		}

		MethodReference getVbHandler(MethodDefinition method, out string eventName) {
			eventName = null;
			if (method.Body == null)
				return null;
			if (method.MethodReturnType.ReturnType.FullName != "System.Void")
				return null;
			if (method.Parameters.Count != 1)
				return null;
			if (method.Body.Variables.Count != 1)
				return null;
			if (!isEventHandlerType(method.Body.Variables[0].VariableType))
				return null;

			var instructions = method.Body.Instructions;
			int index = 0;

			int newobjIndex = findInstruction(instructions, index, Code.Newobj);
			if (newobjIndex == -1 || findInstruction(instructions, newobjIndex + 1, Code.Newobj) != -1)
				return null;
			if (!isEventHandlerCtor(instructions[newobjIndex].Operand as MethodReference))
				return null;
			if (newobjIndex < 1)
				return null;
			var ldvirtftn = instructions[newobjIndex - 1];
			if (ldvirtftn.OpCode.Code != Code.Ldvirtftn && ldvirtftn.OpCode.Code != Code.Ldftn)
				return null;
			var handlerMethod = ldvirtftn.Operand as MethodReference;
			if (handlerMethod == null)
				return null;
			if (!MemberReferenceHelper.compareTypes(method.DeclaringType, handlerMethod.DeclaringType))
				return null;
			index = newobjIndex;

			FieldReference addField, removeField;
			MethodReference addMethod, removeMethod;
			if (!findEventCall(instructions, ref index, out removeField, out removeMethod))
				return null;
			if (!findEventCall(instructions, ref index, out addField, out addMethod))
				return null;

			if (findInstruction(instructions, index, Code.Callvirt) != -1)
				return null;
			if (!MemberReferenceHelper.compareFieldReference(addField, removeField))
				return null;
			if (!MemberReferenceHelper.compareTypes(method.DeclaringType, addField.DeclaringType))
				return null;
			if (!MemberReferenceHelper.compareTypes(addMethod.DeclaringType, removeMethod.DeclaringType))
				return null;
			if (!Utils.StartsWith(addMethod.Name, "add_", StringComparison.Ordinal))
				return null;
			if (!Utils.StartsWith(removeMethod.Name, "remove_", StringComparison.Ordinal))
				return null;
			eventName = addMethod.Name.Substring(4);
			if (eventName != removeMethod.Name.Substring(7))
				return null;
			if (eventName == "")
				return null;

			return handlerMethod;
		}

		static bool findEventCall(IList<Instruction> instructions, ref int index, out FieldReference field, out MethodReference calledMethod) {
			field = null;
			calledMethod = null;

			int callvirt = findInstruction(instructions, index, Code.Callvirt);
			if (callvirt < 2)
				return false;
			index = callvirt + 1;

			var ldloc = instructions[callvirt - 1];
			if (ldloc.OpCode.Code != Code.Ldloc_0)
				return false;

			var ldfld = instructions[callvirt - 2];
			if (ldfld.OpCode.Code != Code.Ldfld)
				return false;

			field = ldfld.Operand as FieldReference;
			calledMethod = instructions[callvirt].Operand as MethodReference;
			return field != null && calledMethod != null;
		}

		static int findInstruction(IList<Instruction> instructions, int index, Code code) {
			for (int i = index; i < instructions.Count; i++) {
				if (instructions[i].OpCode.Code == code)
					return i;
			}
			return -1;
		}

		void initFieldEventHandlers(Dictionary<FieldReferenceAndDeclaringTypeKey, FieldDef> ourFields, Dictionary<MethodReferenceAndDeclaringTypeKey, MethodDef> ourMethods) {
			var variableNameState = MemberRenameState.variableNameState;
			foreach (var methodDef in methods.getAll()) {
				if (methodDef.MethodDefinition.Body == null)
					continue;
				if (methodDef.MethodDefinition.IsStatic)
					continue;
				var instructions = methodDef.MethodDefinition.Body.Instructions;
				for (int i = 0; i < instructions.Count - 6; i++) {
					// We're looking for this code pattern:
					//	ldarg.0
					//	ldfld field
					//	ldarg.0
					//	ldftn method / ldarg.0 + ldvirtftn
					//	newobj event_handler_ctor
					//	callvirt add_SomeEvent

					if (DotNetUtils.getArgIndex(methodDef.MethodDefinition, instructions[i]) != 0)
						continue;
					int index = i + 1;

					var ldfld = instructions[index++];
					if (ldfld.OpCode.Code != Code.Ldfld)
						continue;
					var fieldRef = ldfld.Operand as FieldReference;
					if (fieldRef == null)
						continue;
					FieldDef fieldDef;
					if (!ourFields.TryGetValue(new FieldReferenceAndDeclaringTypeKey(fieldRef), out fieldDef))
						continue;

					if (DotNetUtils.getArgIndex(methodDef.MethodDefinition, instructions[index++]) != 0)
						continue;

					MethodReference methodRef;
					var instr = instructions[index + 1];
					if (instr.OpCode.Code == Code.Ldvirtftn) {
						if (!isThisOrDup(methodDef.MethodDefinition, instructions[index++]))
							continue;
						var ldvirtftn = instructions[index++];
						methodRef = ldvirtftn.Operand as MethodReference;
					}
					else {
						var ldftn = instructions[index++];
						if (ldftn.OpCode.Code != Code.Ldftn)
							continue;
						methodRef = ldftn.Operand as MethodReference;
					}
					if (methodRef == null)
						continue;
					MethodDef handlerMethod;
					if (!ourMethods.TryGetValue(new MethodReferenceAndDeclaringTypeKey(methodRef), out handlerMethod))
						continue;

					var newobj = instructions[index++];
					if (newobj.OpCode.Code != Code.Newobj)
						continue;
					if (!isEventHandlerCtor(newobj.Operand as MethodReference))
						continue;

					var call = instructions[index++];
					if (call.OpCode.Code != Code.Call && call.OpCode.Code != Code.Callvirt)
						continue;
					var addHandler = call.Operand as MethodReference;
					if (addHandler == null)
						continue;
					if (!Utils.StartsWith(addHandler.Name, "add_", StringComparison.Ordinal))
						continue;

					var eventName = addHandler.Name.Substring(4);
					if (!MemberRenameState.variableNameState.IsValidName(eventName))
						continue;

					newMethodNames[handlerMethod] = string.Format("{0}_{1}", fieldDef.NewName, eventName);
				}
			}
		}

		void initTypeEventHandlers(Dictionary<FieldReferenceAndDeclaringTypeKey, FieldDef> ourFields, Dictionary<MethodReferenceAndDeclaringTypeKey, MethodDef> ourMethods) {
			foreach (var methodDef in methods.getAll()) {
				if (methodDef.MethodDefinition.Body == null)
					continue;
				if (methodDef.MethodDefinition.IsStatic)
					continue;
				var method = methodDef.MethodDefinition;
				var instructions = method.Body.Instructions;
				for (int i = 0; i < instructions.Count - 5; i++) {
					// ldarg.0
					// ldarg.0 / dup
					// ldarg.0 / dup
					// ldvirtftn handler
					// newobj event handler ctor
					// call add_Xyz

					if (DotNetUtils.getArgIndex(method, instructions[i]) != 0)
						continue;
					int index = i + 1;

					if (!isThisOrDup(method, instructions[index++]))
						continue;
					MethodReference handler;
					if (instructions[index].OpCode.Code == Code.Ldftn) {
						handler = instructions[index++].Operand as MethodReference;
					}
					else {
						if (!isThisOrDup(method, instructions[index++]))
							continue;
						var instr = instructions[index++];
						if (instr.OpCode.Code != Code.Ldvirtftn)
							continue;
						handler = instr.Operand as MethodReference;
					}
					if (handler == null)
						continue;
					MethodDef handlerDef;
					if (!ourMethods.TryGetValue(new MethodReferenceAndDeclaringTypeKey(handler), out handlerDef))
						continue;

					var newobj = instructions[index++];
					if (newobj.OpCode.Code != Code.Newobj)
						continue;
					if (!isEventHandlerCtor(newobj.Operand as MethodReference))
						continue;

					var call = instructions[index++];
					if (call.OpCode.Code != Code.Call && call.OpCode.Code != Code.Callvirt)
						continue;
					var addMethod = call.Operand as MethodReference;
					if (addMethod == null)
						continue;
					if (!Utils.StartsWith(addMethod.Name, "add_", StringComparison.Ordinal))
						continue;

					var eventName = addMethod.Name.Substring(4);
					if (!MemberRenameState.variableNameState.IsValidName(eventName))
						continue;

					newMethodNames[handlerDef] = string.Format("{0}_{1}", NewName, eventName);
				}
			}
		}

		static bool isThisOrDup(MethodReference method, Instruction instr) {
			return DotNetUtils.getArgIndex(method, instr) == 0 || instr.OpCode.Code == Code.Dup;
		}

		static bool isEventHandlerCtor(MethodReference method) {
			if (method == null)
				return false;
			if (method.Name != ".ctor")
				return false;
			if (!DotNetUtils.isMethod(method, "System.Void", "(System.Object,System.IntPtr)"))
				return false;
			if (!isEventHandlerType(method.DeclaringType))
				return false;
			return true;
		}

		static bool isEventHandlerType(TypeReference type) {
			return type.FullName.EndsWith("EventHandler", StringComparison.Ordinal);
		}

		static MethodReference getOverrideMethod(MethodDefinition meth) {
			if (meth == null || !meth.HasOverrides)
				return null;
			return meth.Overrides[0];
		}

		static string getRealName(string name) {
			int index = name.LastIndexOf('.');
			if (index < 0)
				return name;
			return name.Substring(index + 1);
		}

		static readonly Regex removeGenericsArityRegex = new Regex(@"`[0-9]+");
		static string getOverrideMethodNamePrefix(TypeReference owner) {
			var name = owner.FullName.Replace('/', '.');
			name = removeGenericsArityRegex.Replace(name, "");
			return name + ".";
		}

		static string getOverrideMethodName(TypeReference owner, string name) {
			return getOverrideMethodNamePrefix(owner) + name;
		}

		void prepareRenameProperties() {
			var variableNameState = MemberRenameState.variableNameState;

			foreach (var propDef in properties.getSorted()) {
				if (propDef.Renamed)
					continue;
				propDef.Renamed = true;

				bool isVirtual = propDef.isVirtual();
				string prefix = "";
				string baseName = propDef.OldName;

				string propName = null;
				if (isVirtual)
					getVirtualPropName(propDef, ref prefix, ref propName);
				if (propName == null)
					newPropertyNames.TryGetValue(propDef, out propName);
				if (propName == null && !variableNameState.IsValidName(propDef.OldName))
					propName = variableNameState.getNewPropertyName(propDef.PropertyDefinition);
				if (propName != null) {
					baseName = propName;
					propDef.NewName = prefix + baseName;
				}

				renameSpecialMethod(propDef.PropertyDefinition.GetMethod, prefix + "get_" + baseName);
				renameSpecialMethod(propDef.PropertyDefinition.SetMethod, prefix + "set_" + baseName, "value");

				if (isVirtual)
					MemberRenameState.add(propDef);
			}
		}

		void getVirtualPropName(PropertyDef propDef, ref string prefix, ref string propName) {
			PropertyRef sameDef;
			var overrideMethod = propDef.getOverrideMethod();
			if (overrideMethod != null && (sameDef = defFinder.findProp(overrideMethod)) != null) {
				prefix = getOverrideMethodNamePrefix(sameDef.Owner.TypeDefinition);
				propName = sameDef.NewName;
				return;
			}

			var method = getOverrideMethod(propDef.PropertyDefinition.GetMethod ?? propDef.PropertyDefinition.SetMethod);
			if (method != null) {
				var realName = getRealName(method.Name);
				// Only use the name if the method is not in one of the loaded files, since the
				// name shouldn't be obfuscated.
				if (Regex.IsMatch(realName, @"^[sg]et_.") && defFinder.findProp(method) == null) {
					prefix = getOverrideMethodNamePrefix(method.DeclaringType);
					propName = realName.Substring(4);
					return;
				}
			}

			sameDef = MemberRenameState.get(propDef);
			if (sameDef != null) {
				prefix = "";
				propName = sameDef.NewName;
				return;
			}
		}

		void prepareRenameEvents() {
			var variableNameState = MemberRenameState.variableNameState;

			foreach (var eventDef in events.getSorted()) {
				if (eventDef.Renamed)
					continue;
				eventDef.Renamed = true;

				bool isVirtual = eventDef.isVirtual();
				string prefix = "";
				string baseName = eventDef.OldName;

				string propName = null;
				if (isVirtual)
					getVirtualEventName(eventDef, ref prefix, ref propName);
				if (propName == null && !variableNameState.IsValidName(eventDef.OldName))
					propName = variableNameState.getNewEventName(eventDef.EventDefinition);
				if (propName != null) {
					baseName = propName;
					eventDef.NewName = prefix + baseName;
				}

				renameSpecialMethod(eventDef.EventDefinition.AddMethod, prefix + "add_" + baseName, "value");
				renameSpecialMethod(eventDef.EventDefinition.RemoveMethod, prefix + "remove_" + baseName, "value");
				renameSpecialMethod(eventDef.EventDefinition.InvokeMethod, prefix + "raise_" + baseName);

				if (isVirtual)
					MemberRenameState.add(eventDef);
			}
		}

		void getVirtualEventName(EventDef eventDef, ref string prefix, ref string propName) {
			EventRef sameDef;
			var overrideMethod = eventDef.getOverrideMethod();
			if (overrideMethod != null && (sameDef = defFinder.findEvent(overrideMethod)) != null) {
				prefix = getOverrideMethodNamePrefix(sameDef.Owner.TypeDefinition);
				propName = sameDef.NewName;
				return;
			}

			var method = getOverrideMethod(eventDef.EventDefinition.AddMethod ?? eventDef.EventDefinition.RemoveMethod);
			if (method != null) {
				var realName = getRealName(method.Name);
				// Only use the name if the method is not in one of the loaded files, since the
				// name shouldn't be obfuscated.
				if (Regex.IsMatch(realName, @"^(add|remove)_.") && defFinder.findEvent(method) == null) {
					prefix = getOverrideMethodNamePrefix(method.DeclaringType);
					propName = realName.Substring(realName.IndexOf('_') + 1);
					return;
				}
			}

			sameDef = MemberRenameState.get(eventDef);
			if (sameDef != null) {
				prefix = "";
				propName = sameDef.NewName;
				return;
			}
		}

		void renameSpecialMethod(MethodDefinition methodDefinition, string newName, string newArgName = null) {
			if (methodDefinition == null)
				return;

			var methodDef = find(methodDefinition);
			if (methodDef == null)
				throw new ApplicationException("Could not find the event/prop method");

			renameMethod(methodDef, newName);

			if (newArgName != null && methodDef.ParamDefs.Count > 0) {
				var arg = methodDef.ParamDefs[methodDef.ParamDefs.Count - 1];
				if (!MemberRenameState.variableNameState.IsValidName(arg.OldName)) {
					arg.NewName = newArgName;
					arg.Renamed = true;
				}
			}
		}

		void prepareRenameMethods() {
			foreach (var methodDef in methods.getSorted())
				renameMethod(methodDef);
		}

		void renameMethod(MethodDef methodDef, string suggestedName = null) {
			if (methodDef.Renamed)
				return;
			methodDef.Renamed = true;

			bool canRenameMethodName = true;
			if (suggestedName == null)
				newMethodNames.TryGetValue(methodDef, out suggestedName);

			if (IsDelegate) {
				switch (methodDef.MethodDefinition.Name) {
				case "BeginInvoke":
				case "EndInvoke":
				case "Invoke":
					canRenameMethodName = false;
					break;
				}
			}

			var variableNameState = MemberRenameState.variableNameState;

			if (canRenameMethodName) {
				var nameCreator = getMethodNameCreator(methodDef, suggestedName);
				if (!methodDef.MethodDefinition.IsRuntimeSpecialName && !variableNameState.IsValidName(methodDef.OldName)) {
					bool useNameCreator = methodDef.isVirtual() || methodDef.Property != null || methodDef.Event != null;
					if (useNameCreator)
						methodDef.NewName = nameCreator.newName();
					else
						methodDef.NewName = variableNameState.getNewMethodName(methodDef.OldName, nameCreator);
				}
			}

			if (methodDef.ParamDefs.Count > 0) {
				if (isEventHandler(methodDef)) {
					methodDef.ParamDefs[0].NewName = "sender";
					methodDef.ParamDefs[0].Renamed = true;
					methodDef.ParamDefs[1].NewName = "e";
					methodDef.ParamDefs[1].Renamed = true;
				}
				else {
					var newVariableNameState = variableNameState.clone();
					foreach (var paramDef in methodDef.ParamDefs) {
						if (!newVariableNameState.IsValidName(paramDef.OldName)) {
							paramDef.NewName = newVariableNameState.getNewParamName(paramDef.OldName, paramDef.ParameterDefinition);
							paramDef.Renamed = true;
						}
					}
				}
			}

			prepareRenameGenericParams(methodDef.GenericParams, variableNameState.IsValidName, methodDef.Owner == null ? null : methodDef.Owner.genericParams);

			if (methodDef.isVirtual())
				MemberRenameState.add(methodDef);
		}

		static bool isEventHandler(MethodDef methodDef) {
			if (methodDef.MethodDefinition.Parameters.Count != 2)
				return false;
			if (methodDef.MethodDefinition.MethodReturnType.ReturnType.FullName != "System.Void")
				return false;
			if (methodDef.MethodDefinition.Parameters[0].ParameterType.FullName != "System.Object")
				return false;
			if (!methodDef.MethodDefinition.Parameters[1].ParameterType.FullName.Contains("EventArgs"))
				return false;
			return true;
		}

		string getPinvokeName(MethodDef methodDef) {
			var entryPoint = methodDef.MethodDefinition.PInvokeInfo.EntryPoint;
			if (Regex.IsMatch(entryPoint, @"^#\d+$"))
				entryPoint = DotNetUtils.getDllName(methodDef.MethodDefinition.PInvokeInfo.Module.Name) + "_" + entryPoint.Substring(1);
			return entryPoint;
		}

		INameCreator getMethodNameCreator(MethodDef methodDef, string suggestedName) {
			var variableNameState = MemberRenameState.variableNameState;
			INameCreator nameCreator = null;
			string newName = null;

			if (methodDef.MethodDefinition.PInvokeInfo != null)
				newName = getPinvokeName(methodDef);
			else if (methodDef.MethodDefinition.IsStatic)
				nameCreator = variableNameState.staticMethodNameCreator;
			else if (methodDef.isVirtual()) {
				MethodRef otherMethodRef;
				if ((otherMethodRef = MemberRenameState.get(methodDef)) != null)
					newName = otherMethodRef.NewName;
				else if (methodDef.MethodDefinition.HasOverrides) {
					var overrideMethod = methodDef.MethodDefinition.Overrides[0];
					var otherMethodDef = defFinder.findMethod(overrideMethod);
					if (otherMethodDef != null)
						newName = getOverrideMethodName(overrideMethod.DeclaringType, otherMethodDef.NewName);
					else
						newName = getOverrideMethodName(overrideMethod.DeclaringType, overrideMethod.Name);
				}
				else
					nameCreator = variableNameState.virtualMethodNameCreator;
			}
			else
				nameCreator = variableNameState.instanceMethodNameCreator;

			if (newName == null)
				newName = suggestedName;
			if (newName != null) {
				if (methodDef.isVirtual())
					nameCreator = new OneNameCreator(newName);	// It must have this name
				else
					nameCreator = new NameCreator2(newName);
			}

			return nameCreator;
		}
	}
}
