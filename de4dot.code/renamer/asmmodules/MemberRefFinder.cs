/*
    Copyright (C) 2011-2014 de4dot@gmail.com

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
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.renamer.asmmodules {
	enum ObjectType {
		Unknown,
		EventDef,
		FieldDef,
		GenericParam,
		MemberRef,
		MethodDef,
		MethodSpec,
		PropertyDef,
		TypeDef,
		TypeRef,
		TypeSig,
		TypeSpec,
		ExportedType,
	}

	class MemberRefFinder {
		public Dictionary<CustomAttribute, bool> customAttributes = new Dictionary<CustomAttribute, bool>();
		public Dictionary<EventDef, bool> eventDefs = new Dictionary<EventDef, bool>();
		public Dictionary<FieldDef, bool> fieldDefs = new Dictionary<FieldDef, bool>();
		public Dictionary<GenericParam, bool> genericParams = new Dictionary<GenericParam, bool>();
		public Dictionary<MemberRef, bool> memberRefs = new Dictionary<MemberRef, bool>();
		public Dictionary<MethodDef, bool> methodDefs = new Dictionary<MethodDef, bool>();
		public Dictionary<MethodSpec, bool> methodSpecs = new Dictionary<MethodSpec, bool>();
		public Dictionary<PropertyDef, bool> propertyDefs = new Dictionary<PropertyDef, bool>();
		public Dictionary<TypeDef, bool> typeDefs = new Dictionary<TypeDef, bool>();
		public Dictionary<TypeRef, bool> typeRefs = new Dictionary<TypeRef, bool>();
		public Dictionary<TypeSig, bool> typeSigs = new Dictionary<TypeSig, bool>();
		public Dictionary<TypeSpec, bool> typeSpecs = new Dictionary<TypeSpec, bool>();
		public Dictionary<ExportedType, bool> exportedTypes = new Dictionary<ExportedType, bool>();

		Stack<object> objectStack;
		ModuleDef validModule;

		public void RemoveTypeDef(TypeDef td) {
			if (!typeDefs.Remove(td))
				throw new ApplicationException(string.Format("Could not remove TypeDef: {0}", td));
		}

		public void RemoveEventDef(EventDef ed) {
			if (!eventDefs.Remove(ed))
				throw new ApplicationException(string.Format("Could not remove EventDef: {0}", ed));
		}

		public void RemoveFieldDef(FieldDef fd) {
			if (!fieldDefs.Remove(fd))
				throw new ApplicationException(string.Format("Could not remove FieldDef: {0}", fd));
		}

		public void RemoveMethodDef(MethodDef md) {
			if (!methodDefs.Remove(md))
				throw new ApplicationException(string.Format("Could not remove MethodDef: {0}", md));
		}

		public void RemovePropertyDef(PropertyDef pd) {
			if (!propertyDefs.Remove(pd))
				throw new ApplicationException(string.Format("Could not remove PropertyDef: {0}", pd));
		}

		public void FindAll(ModuleDef module) {
			validModule = module;

			// This needs to be big. About 2048 entries should be enough for most though...
			objectStack = new Stack<object>(0x1000);

			Add(module);
			ProcessAll();

			objectStack = null;
		}

		void Push(object mr) {
			if (mr == null)
				return;
			objectStack.Push(mr);
		}

		void ProcessAll() {
			while (objectStack.Count > 0) {
				var o = objectStack.Pop();
				switch (GetObjectType(o)) {
				case ObjectType.Unknown: break;
				case ObjectType.EventDef:	Add((EventDef)o); break;
				case ObjectType.FieldDef:	Add((FieldDef)o); break;
				case ObjectType.GenericParam: Add((GenericParam)o); break;
				case ObjectType.MemberRef:	Add((MemberRef)o); break;
				case ObjectType.MethodDef:	Add((MethodDef)o); break;
				case ObjectType.MethodSpec:	Add((MethodSpec)o); break;
				case ObjectType.PropertyDef:Add((PropertyDef)o); break;
				case ObjectType.TypeDef:	Add((TypeDef)o); break;
				case ObjectType.TypeRef:	Add((TypeRef)o); break;
				case ObjectType.TypeSig:	Add((TypeSig)o); break;
				case ObjectType.TypeSpec:	Add((TypeSpec)o); break;
				case ObjectType.ExportedType: Add((ExportedType)o); break;
				default: throw new InvalidOperationException(string.Format("Unknown type: {0}", o.GetType()));
				}
			}
		}

		readonly Dictionary<Type, ObjectType> toObjectType = new Dictionary<Type, ObjectType>();
		ObjectType GetObjectType(object o) {
			if (o == null)
				return ObjectType.Unknown;
			var type = o.GetType();
			ObjectType mrType;
			if (toObjectType.TryGetValue(type, out mrType))
				return mrType;
			mrType = GetObjectType2(o);
			toObjectType[type] = mrType;
			return mrType;
		}

		static ObjectType GetObjectType2(object o) {
			if (o is EventDef)		return ObjectType.EventDef;
			if (o is FieldDef)		return ObjectType.FieldDef;
			if (o is GenericParam)	return ObjectType.GenericParam;
			if (o is MemberRef)		return ObjectType.MemberRef;
			if (o is MethodDef)		return ObjectType.MethodDef;
			if (o is MethodSpec)	return ObjectType.MethodSpec;
			if (o is PropertyDef)	return ObjectType.PropertyDef;
			if (o is TypeDef)		return ObjectType.TypeDef;
			if (o is TypeRef)		return ObjectType.TypeRef;
			if (o is TypeSig)		return ObjectType.TypeSig;
			if (o is TypeSpec)		return ObjectType.TypeSpec;
			if (o is ExportedType)	return ObjectType.ExportedType;
			return ObjectType.Unknown;
		}

		void Add(ModuleDef mod) {
			Push(mod.ManagedEntryPoint);
			Add(mod.CustomAttributes);
			Add(mod.Types);
			Add(mod.ExportedTypes);
			if (mod.IsManifestModule)
				Add(mod.Assembly);
			Add(mod.VTableFixups);
		}

		void Add(VTableFixups fixups) {
			if (fixups == null)
				return;
			foreach (var fixup in fixups) {
				foreach (var method in fixup)
					Push(method);
			}
		}

		void Add(AssemblyDef asm) {
			if (asm == null)
				return;
			Add(asm.DeclSecurities);
			Add(asm.CustomAttributes);
		}

		void Add(CallingConventionSig sig) {
			if (sig == null)
				return;

			var fs = sig as FieldSig;
			if (fs != null) {
				Add(fs);
				return;
			}

			var mbs = sig as MethodBaseSig;
			if (mbs != null) {
				Add(mbs);
				return;
			}

			var ls = sig as LocalSig;
			if (ls != null) {
				Add(ls);
				return;
			}

			var gims = sig as GenericInstMethodSig;
			if (gims != null) {
				Add(gims);
				return;
			}
		}

		void Add(FieldSig sig) {
			if (sig == null)
				return;
			Add(sig.Type);
		}

		void Add(MethodBaseSig sig) {
			if (sig == null)
				return;
			Add(sig.RetType);
			Add(sig.Params);
			Add(sig.ParamsAfterSentinel);
		}

		void Add(LocalSig sig) {
			if (sig == null)
				return;
			Add(sig.Locals);
		}

		void Add(GenericInstMethodSig sig) {
			if (sig == null)
				return;
			Add(sig.GenericArguments);
		}

		void Add(IEnumerable<CustomAttribute> cas) {
			if (cas == null)
				return;
			foreach (var ca in cas)
				Add(ca);
		}

		void Add(CustomAttribute ca) {
			if (ca == null || customAttributes.ContainsKey(ca))
				return;
			customAttributes[ca] = true;
			Push(ca.Constructor);
			Add(ca.ConstructorArguments);
			Add(ca.NamedArguments);
		}

		void Add(IEnumerable<CAArgument> args) {
			if (args == null)
				return;
			foreach (var arg in args)
				Add(arg);
		}

		void Add(CAArgument arg) {
			// It's a struct so can't be null
			Add(arg.Type);
		}

		void Add(IEnumerable<CANamedArgument> args) {
			if (args == null)
				return;
			foreach (var arg in args)
				Add(arg);
		}

		void Add(CANamedArgument arg) {
			if (arg == null)
				return;
			Add(arg.Type);
			Add(arg.Argument);
		}

		void Add(IEnumerable<DeclSecurity> decls) {
			if (decls == null)
				return;
			foreach (var decl in decls)
				Add(decl);
		}

		void Add(DeclSecurity decl) {
			if (decl == null)
				return;
			Add(decl.CustomAttributes);
		}

		void Add(IEnumerable<EventDef> eds) {
			if (eds == null)
				return;
			foreach (var ed in eds)
				Add(ed);
		}

		void Add(EventDef ed) {
			if (ed == null || eventDefs.ContainsKey(ed))
				return;
			if (ed.DeclaringType != null && ed.DeclaringType.Module != validModule)
				return;
			eventDefs[ed] = true;
			Push(ed.EventType);
			Add(ed.CustomAttributes);
			Add(ed.AddMethod);
			Add(ed.InvokeMethod);
			Add(ed.RemoveMethod);
			Add(ed.OtherMethods);
			Add(ed.DeclaringType);
		}

		void Add(IEnumerable<FieldDef> fds) {
			if (fds == null)
				return;
			foreach (var fd in fds)
				Add(fd);
		}

		void Add(FieldDef fd) {
			if (fd == null || fieldDefs.ContainsKey(fd))
				return;
			if (fd.DeclaringType != null && fd.DeclaringType.Module != validModule)
				return;
			fieldDefs[fd] = true;
			Add(fd.CustomAttributes);
			Add(fd.Signature);
			Add(fd.DeclaringType);
		}

		void Add(IEnumerable<GenericParam> gps) {
			if (gps == null)
				return;
			foreach (var gp in gps)
				Add(gp);
		}

		void Add(GenericParam gp) {
			if (gp == null || genericParams.ContainsKey(gp))
				return;
			genericParams[gp] = true;
			Push(gp.Owner);
			Push(gp.Kind);
			Add(gp.GenericParamConstraints);
			Add(gp.CustomAttributes);
		}

		void Add(IEnumerable<GenericParamConstraint> gpcs) {
			if (gpcs == null)
				return;
			foreach (var gpc in gpcs)
				Add(gpc);
		}

		void Add(GenericParamConstraint gpc) {
			if (gpc == null)
				return;
			Add(gpc.Owner);
			Push(gpc.Constraint);
			Add(gpc.CustomAttributes);
		}

		void Add(MemberRef mr) {
			if (mr == null || memberRefs.ContainsKey(mr))
				return;
			if (mr.Module != validModule)
				return;
			memberRefs[mr] = true;
			Push(mr.Class);
			Add(mr.Signature);
			Add(mr.CustomAttributes);
		}

		void Add(IEnumerable<MethodDef> methods) {
			if (methods == null)
				return;
			foreach (var m in methods)
				Add(m);
		}

		void Add(MethodDef md) {
			if (md == null || methodDefs.ContainsKey(md))
				return;
			if (md.DeclaringType != null && md.DeclaringType.Module != validModule)
				return;
			methodDefs[md] = true;
			Add(md.Signature);
			Add(md.ParamDefs);
			Add(md.GenericParameters);
			Add(md.DeclSecurities);
			Add(md.MethodBody);
			Add(md.CustomAttributes);
			Add(md.Overrides);
			Add(md.DeclaringType);
		}

		void Add(MethodBody mb) {
			var cb = mb as CilBody;
			if (cb != null)
				Add(cb);
		}

		void Add(CilBody cb) {
			if (cb == null)
				return;
			Add(cb.Instructions);
			Add(cb.ExceptionHandlers);
			Add(cb.Variables);
		}

		void Add(IEnumerable<Instruction> instrs) {
			if (instrs == null)
				return;
			foreach (var instr in instrs) {
				if (instr == null)
					continue;
				switch (instr.OpCode.OperandType) {
				case OperandType.InlineTok:
				case OperandType.InlineType:
				case OperandType.InlineMethod:
				case OperandType.InlineField:
					Push(instr.Operand);
					break;

				case OperandType.InlineSig:
					Add(instr.Operand as CallingConventionSig);
					break;

				case OperandType.InlineVar:
				case OperandType.ShortInlineVar:
					var local = instr.Operand as Local;
					if (local != null) {
						Add(local);
						break;
					}
					var arg = instr.Operand as Parameter;
					if (arg != null) {
						Add(arg);
						break;
					}
					break;
				}
			}
		}

		void Add(IEnumerable<ExceptionHandler> ehs) {
			if (ehs == null)
				return;
			foreach (var eh in ehs)
				Push(eh.CatchType);
		}

		void Add(IEnumerable<Local> locals) {
			if (locals == null)
				return;
			foreach (var local in locals)
				Add(local);
		}

		void Add(Local local) {
			if (local == null)
				return;
			Add(local.Type);
		}

		void Add(IEnumerable<Parameter> ps) {
			if (ps == null)
				return;
			foreach (var p in ps)
				Add(p);
		}

		void Add(Parameter param) {
			if (param == null)
				return;
			Add(param.Type);
			Add(param.Method);
		}

		void Add(IEnumerable<ParamDef> pds) {
			if (pds == null)
				return;
			foreach (var pd in pds)
				Add(pd);
		}

		void Add(ParamDef pd) {
			if (pd == null)
				return;
			Add(pd.DeclaringMethod);
			Add(pd.CustomAttributes);
		}

		void Add(IEnumerable<MethodOverride> mos) {
			if (mos == null)
				return;
			foreach (var mo in mos)
				Add(mo);
		}

		void Add(MethodOverride mo) {
			// It's a struct so can't be null
			Push(mo.MethodBody);
			Push(mo.MethodDeclaration);
		}

		void Add(MethodSpec ms) {
			if (ms == null || methodSpecs.ContainsKey(ms))
				return;
			if (ms.Method != null && ms.Method.DeclaringType != null && ms.Method.DeclaringType.Module != validModule)
				return;
			methodSpecs[ms] = true;
			Push(ms.Method);
			Add(ms.Instantiation);
			Add(ms.CustomAttributes);
		}

		void Add(IEnumerable<PropertyDef> pds) {
			if (pds == null)
				return;
			foreach (var pd in pds)
				Add(pd);
		}

		void Add(PropertyDef pd) {
			if (pd == null || propertyDefs.ContainsKey(pd))
				return;
			if (pd.DeclaringType != null && pd.DeclaringType.Module != validModule)
				return;
			propertyDefs[pd] = true;
			Add(pd.Type);
			Add(pd.CustomAttributes);
			Add(pd.GetMethod);
			Add(pd.SetMethod);
			Add(pd.OtherMethods);
			Add(pd.DeclaringType);
		}

		void Add(IEnumerable<TypeDef> tds) {
			if (tds == null)
				return;
			foreach (var td in tds)
				Add(td);
		}

		void Add(TypeDef td) {
			if (td == null || typeDefs.ContainsKey(td))
				return;
			if (td.Module != validModule)
				return;
			typeDefs[td] = true;
			Push(td.BaseType);
			Add(td.Fields);
			Add(td.Methods);
			Add(td.GenericParameters);
			Add(td.Interfaces);
			Add(td.DeclSecurities);
			Add(td.DeclaringType);
			Add(td.Events);
			Add(td.Properties);
			Add(td.NestedTypes);
			Add(td.CustomAttributes);
		}

		void Add(IEnumerable<InterfaceImpl> iis) {
			if (iis == null)
				return;
			foreach (var ii in iis)
				Add(ii);
		}

		void Add(InterfaceImpl ii) {
			if (ii == null)
				return;
			Push(ii.Interface);
			Add(ii.CustomAttributes);
		}

		void Add(TypeRef tr) {
			if (tr == null || typeRefs.ContainsKey(tr))
				return;
			if (tr.Module != validModule)
				return;
			typeRefs[tr] = true;
			Push(tr.ResolutionScope);
			Add(tr.CustomAttributes);
		}

		void Add(IEnumerable<TypeSig> tss) {
			if (tss == null)
				return;
			foreach (var ts in tss)
				Add(ts);
		}

		void Add(TypeSig ts) {
			if (ts == null || typeSigs.ContainsKey(ts))
				return;
			if (ts.Module != validModule)
				return;
			typeSigs[ts] = true;

			for (; ts != null; ts = ts.Next) {
				switch (ts.ElementType) {
				case ElementType.Void:
				case ElementType.Boolean:
				case ElementType.Char:
				case ElementType.I1:
				case ElementType.U1:
				case ElementType.I2:
				case ElementType.U2:
				case ElementType.I4:
				case ElementType.U4:
				case ElementType.I8:
				case ElementType.U8:
				case ElementType.R4:
				case ElementType.R8:
				case ElementType.String:
				case ElementType.ValueType:
				case ElementType.Class:
				case ElementType.TypedByRef:
				case ElementType.I:
				case ElementType.U:
				case ElementType.Object:
					var tdrs = (TypeDefOrRefSig)ts;
					Push(tdrs.TypeDefOrRef);
					break;

				case ElementType.FnPtr:
					var fps = (FnPtrSig)ts;
					Add(fps.Signature);
					break;

				case ElementType.GenericInst:
					var gis = (GenericInstSig)ts;
					Add(gis.GenericType);
					Add(gis.GenericArguments);
					break;

				case ElementType.CModReqd:
				case ElementType.CModOpt:
					var ms = (ModifierSig)ts;
					Push(ms.Modifier);
					break;

				case ElementType.End:
				case ElementType.Ptr:
				case ElementType.ByRef:
				case ElementType.Var:
				case ElementType.Array:
				case ElementType.ValueArray:
				case ElementType.R:
				case ElementType.SZArray:
				case ElementType.MVar:
				case ElementType.Internal:
				case ElementType.Module:
				case ElementType.Sentinel:
				case ElementType.Pinned:
				default:
					break;
				}
			}
		}

		void Add(TypeSpec ts) {
			if (ts == null || typeSpecs.ContainsKey(ts))
				return;
			if (ts.Module != validModule)
				return;
			typeSpecs[ts] = true;
			Add(ts.TypeSig);
			Add(ts.CustomAttributes);
		}

		void Add(IEnumerable<ExportedType> ets) {
			if (ets == null)
				return;
			foreach (var et in ets)
				Add(et);
		}

		void Add(ExportedType et) {
			if (et == null || exportedTypes.ContainsKey(et))
				return;
			if (et.Module != validModule)
				return;
			exportedTypes[et] = true;
			Add(et.CustomAttributes);
			Push(et.Implementation);
		}
	}
}
