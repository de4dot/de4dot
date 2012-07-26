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

using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.renamer.asmmodules {
	class MemberRefFinder {
		public Dictionary<EventDefinition, bool> eventDefinitions = new Dictionary<EventDefinition, bool>();
		public Dictionary<FieldReference, bool> fieldReferences = new Dictionary<FieldReference, bool>();
		public Dictionary<FieldDefinition, bool> fieldDefinitions = new Dictionary<FieldDefinition, bool>();
		public Dictionary<MethodReference, bool> methodReferences = new Dictionary<MethodReference, bool>();
		public Dictionary<MethodDefinition, bool> methodDefinitions = new Dictionary<MethodDefinition, bool>();
		public Dictionary<GenericInstanceMethod, bool> genericInstanceMethods = new Dictionary<GenericInstanceMethod, bool>();
		public Dictionary<PropertyDefinition, bool> propertyDefinitions = new Dictionary<PropertyDefinition, bool>();
		public Dictionary<TypeReference, bool> typeReferences = new Dictionary<TypeReference, bool>();
		public Dictionary<TypeDefinition, bool> typeDefinitions = new Dictionary<TypeDefinition, bool>();
		public Dictionary<GenericParameter, bool> genericParameters = new Dictionary<GenericParameter, bool>();
		public Dictionary<ArrayType, bool> arrayTypes = new Dictionary<ArrayType, bool>();
		public Dictionary<FunctionPointerType, bool> functionPointerTypes = new Dictionary<FunctionPointerType, bool>();
		public Dictionary<GenericInstanceType, bool> genericInstanceTypes = new Dictionary<GenericInstanceType, bool>();
		public Dictionary<OptionalModifierType, bool> optionalModifierTypes = new Dictionary<OptionalModifierType, bool>();
		public Dictionary<RequiredModifierType, bool> requiredModifierTypes = new Dictionary<RequiredModifierType, bool>();
		public Dictionary<PinnedType, bool> pinnedTypes = new Dictionary<PinnedType, bool>();
		public Dictionary<PointerType, bool> pointerTypes = new Dictionary<PointerType, bool>();
		public Dictionary<ByReferenceType, bool> byReferenceTypes = new Dictionary<ByReferenceType, bool>();
		public Dictionary<SentinelType, bool> sentinelTypes = new Dictionary<SentinelType, bool>();
		public Dictionary<CustomAttribute, bool> customAttributes = new Dictionary<CustomAttribute, bool>();

		Stack<MemberReference> memberRefStack;
		ModuleDefinition validModule;

		public void removeTypeDefinition(TypeDefinition td) {
			if (!typeDefinitions.Remove(td))
				throw new ApplicationException(string.Format("Could not remove TypeDefinition: {0}", td));
		}

		public void removeEventDefinition(EventDefinition ed) {
			if (!eventDefinitions.Remove(ed))
				throw new ApplicationException(string.Format("Could not remove EventDefinition: {0}", ed));
		}

		public void removeFieldDefinition(FieldDefinition fd) {
			if (!fieldDefinitions.Remove(fd))
				throw new ApplicationException(string.Format("Could not remove FieldDefinition: {0}", fd));
		}

		public void removeMethodDefinition(MethodDefinition md) {
			if (!methodDefinitions.Remove(md))
				throw new ApplicationException(string.Format("Could not remove MethodDefinition: {0}", md));
		}

		public void removePropertyDefinition(PropertyDefinition pd) {
			if (!propertyDefinitions.Remove(pd))
				throw new ApplicationException(string.Format("Could not remove PropertyDefinition: {0}", pd));
		}

		public void findAll(ModuleDefinition module, IEnumerable<TypeDefinition> types) {
			validModule = module;

			// This needs to be big. About 2048 entries should be enough for most though...
			memberRefStack = new Stack<MemberReference>(0x1000);

			foreach (var type in types)
				pushMember(type);

			addModule(module);
			processAll();

			memberRefStack = null;
		}

		Dictionary<string, bool> exceptionMessages = new Dictionary<string, bool>(StringComparer.Ordinal);
		void access(Action action) {
			string exMessage = null;
			try {
				action();
			}
			catch (ResolutionException ex) {
				exMessage = ex.Message;
			}
			catch (AssemblyResolutionException ex) {
				exMessage = ex.Message;
			}
			if (exMessage != null) {
				if (!exceptionMessages.ContainsKey(exMessage)) {
					exceptionMessages[exMessage] = true;
					Log.w("Could not resolve a reference. ERROR: {0}", exMessage);
				}
			}
		}

		void pushMember(MemberReference memberReference) {
			if (memberReference == null)
				return;
			if (memberReference.Module != validModule)
				return;
			memberRefStack.Push(memberReference);
		}

		void addModule(ModuleDefinition module) {
			pushMember(module.EntryPoint);
			access(() => addCustomAttributes(module.CustomAttributes));
			if (module.Assembly != null && module == module.Assembly.MainModule) {
				var asm = module.Assembly;
				access(() => addCustomAttributes(asm.CustomAttributes));
				addSecurityDeclarations(asm.SecurityDeclarations);
			}
		}

		void processAll() {
			while (memberRefStack.Count > 0)
				process(memberRefStack.Pop());
		}

		void process(MemberReference memberRef) {
			if (memberRef == null)
				return;

			var type = MemberReferenceHelper.getMemberReferenceType(memberRef);
			switch (type) {
			case CecilType.ArrayType:
				doArrayType((ArrayType)memberRef);
				break;
			case CecilType.ByReferenceType:
				doByReferenceType((ByReferenceType)memberRef);
				break;
			case CecilType.EventDefinition:
				doEventDefinition((EventDefinition)memberRef);
				break;
			case CecilType.FieldDefinition:
				doFieldDefinition((FieldDefinition)memberRef);
				break;
			case CecilType.FieldReference:
				doFieldReference((FieldReference)memberRef);
				break;
			case CecilType.FunctionPointerType:
				doFunctionPointerType((FunctionPointerType)memberRef);
				break;
			case CecilType.GenericInstanceMethod:
				doGenericInstanceMethod((GenericInstanceMethod)memberRef);
				break;
			case CecilType.GenericInstanceType:
				doGenericInstanceType((GenericInstanceType)memberRef);
				break;
			case CecilType.GenericParameter:
				doGenericParameter((GenericParameter)memberRef);
				break;
			case CecilType.MethodDefinition:
				doMethodDefinition((MethodDefinition)memberRef);
				break;
			case CecilType.MethodReference:
				doMethodReference((MethodReference)memberRef);
				break;
			case CecilType.OptionalModifierType:
				doOptionalModifierType((OptionalModifierType)memberRef);
				break;
			case CecilType.PinnedType:
				doPinnedType((PinnedType)memberRef);
				break;
			case CecilType.PointerType:
				doPointerType((PointerType)memberRef);
				break;
			case CecilType.PropertyDefinition:
				doPropertyDefinition((PropertyDefinition)memberRef);
				break;
			case CecilType.RequiredModifierType:
				doRequiredModifierType((RequiredModifierType)memberRef);
				break;
			case CecilType.SentinelType:
				doSentinelType((SentinelType)memberRef);
				break;
			case CecilType.TypeDefinition:
				doTypeDefinition((TypeDefinition)memberRef);
				break;
			case CecilType.TypeReference:
				doTypeReference((TypeReference)memberRef);
				break;
			default:
				throw new ApplicationException(string.Format("Unknown cecil type {0}", type));
			}
		}

		void addCustomAttributes(IEnumerable<CustomAttribute> attributes) {
			if (attributes == null)
				return;
			foreach (var attr in attributes)
				addCustomAttribute(attr);
		}
		void addCustomAttributeArguments(IEnumerable<CustomAttributeArgument> args) {
			if (args == null)
				return;
			foreach (var arg in args)
				addCustomAttributeArgument(arg);
		}
		void addCustomAttributeNamedArguments(IEnumerable<CustomAttributeNamedArgument> args) {
			if (args == null)
				return;
			foreach (var arg in args)
				addCustomAttributeNamedArgument(arg);
		}
		void addParameterDefinitions(IEnumerable<ParameterDefinition> parameters) {
			if (parameters == null)
				return;
			foreach (var param in parameters)
				addParameterDefinition(param);
		}
		void addSecurityDeclarations(IEnumerable<SecurityDeclaration> decls) {
			if (decls == null)
				return;
			foreach (var decl in decls)
				addSecurityDeclaration(decl);
		}
		void addSecurityAttributes(IEnumerable<SecurityAttribute> attrs) {
			if (attrs == null)
				return;
			foreach (var attr in attrs)
				addSecurityAttribute(attr);
		}
		void addExceptionHandlers(IEnumerable<ExceptionHandler> handlers) {
			if (handlers == null)
				return;
			foreach (var h in handlers)
				addExceptionHandler(h);
		}
		void addVariableDefinitions(IEnumerable<VariableDefinition> vars) {
			if (vars == null)
				return;
			foreach (var v in vars)
				addVariableDefinition(v);
		}
		void addScopes(IEnumerable<Scope> scopes) {
			if (scopes == null)
				return;
			foreach (var s in scopes)
				addScope(s);
		}
		void addInstructions(IEnumerable<Instruction> instrs) {
			if (instrs == null)
				return;
			foreach (var instr in instrs) {
				switch (instr.OpCode.OperandType) {
				case OperandType.InlineTok:
				case OperandType.InlineType:
				case OperandType.InlineMethod:
				case OperandType.InlineField:
					pushMember(instr.Operand as MemberReference);
					break;
				case OperandType.InlineSig:
					addCallSite(instr.Operand as CallSite);
					break;
				case OperandType.InlineVar:
				case OperandType.ShortInlineVar:
					addVariableDefinition(instr.Operand as VariableDefinition);
					break;
				case OperandType.InlineArg:
				case OperandType.ShortInlineArg:
					addParameterDefinition(instr.Operand as ParameterDefinition);
					break;
				}
			}
		}
		void addTypeReferences(IEnumerable<TypeReference> types) {
			if (types == null)
				return;
			foreach (var typeRef in types)
				pushMember(typeRef);
		}
		void addTypeDefinitions(IEnumerable<TypeDefinition> types) {
			if (types == null)
				return;
			foreach (var type in types)
				pushMember(type);
		}
		void addMethodReferences(IEnumerable<MethodReference> methodRefs) {
			if (methodRefs == null)
				return;
			foreach (var m in methodRefs)
				pushMember(m);
		}
		void addMethodDefinitions(IEnumerable<MethodDefinition> methods) {
			if (methods == null)
				return;
			foreach (var m in methods)
				pushMember(m);
		}
		void addGenericParameters(IEnumerable<GenericParameter> parameters) {
			if (parameters == null)
				return;
			foreach (var param in parameters)
				pushMember(param);
		}
		void addFieldDefinitions(IEnumerable<FieldDefinition> fields) {
			if (fields == null)
				return;
			foreach (var f in fields)
				pushMember(f);
		}
		void addEventDefinitions(IEnumerable<EventDefinition> events) {
			if (events == null)
				return;
			foreach (var e in events)
				pushMember(e);
		}
		void addPropertyDefinitions(IEnumerable<PropertyDefinition> props) {
			if (props == null)
				return;
			foreach (var p in props)
				pushMember(p);
		}
		void addMemberReference(MemberReference memberReference) {
			if (memberReference == null)
				return;
			pushMember(memberReference.DeclaringType);
		}
		void addEventReference(EventReference eventReference) {
			if (eventReference == null)
				return;
			addMemberReference(eventReference);
			pushMember(eventReference.EventType);
		}
		void addEventDefinition(EventDefinition eventDefinition) {
			if (eventDefinition == null)
				return;
			addEventReference(eventDefinition);
			pushMember(eventDefinition.AddMethod);
			pushMember(eventDefinition.InvokeMethod);
			pushMember(eventDefinition.RemoveMethod);
			addMethodDefinitions(eventDefinition.OtherMethods);
			access(() => addCustomAttributes(eventDefinition.CustomAttributes));
		}
		void addCustomAttribute(CustomAttribute attr) {
			if (attr == null)
				return;
			customAttributes[attr] = true;
			pushMember(attr.Constructor);

			// Some obfuscators don't rename custom ctor arguments to the new name, causing
			// Mono.Cecil to use a null reference.
			try { access(() => addCustomAttributeArguments(attr.ConstructorArguments)); } catch (NullReferenceException) { }
			try { access(() => addCustomAttributeNamedArguments(attr.Fields)); } catch (NullReferenceException) { }
			try { access(() => addCustomAttributeNamedArguments(attr.Properties)); } catch (NullReferenceException) { }
		}
		void addCustomAttributeArgument(CustomAttributeArgument arg) {
			pushMember(arg.Type);
		}
		void addCustomAttributeNamedArgument(CustomAttributeNamedArgument field) {
			addCustomAttributeArgument(field.Argument);
		}
		void addFieldReference(FieldReference fieldReference) {
			if (fieldReference == null)
				return;
			addMemberReference(fieldReference);
			pushMember(fieldReference.FieldType);
		}
		void addFieldDefinition(FieldDefinition fieldDefinition) {
			if (fieldDefinition == null)
				return;
			addFieldReference(fieldDefinition);
			access(() => addCustomAttributes(fieldDefinition.CustomAttributes));
		}
		void addMethodReference(MethodReference methodReference) {
			if (methodReference == null)
				return;
			addMemberReference(methodReference);
			addParameterDefinitions(methodReference.Parameters);
			addMethodReturnType(methodReference.MethodReturnType);
			addGenericParameters(methodReference.GenericParameters);
		}
		void addParameterReference(ParameterReference param) {
			if (param == null)
				return;
			pushMember(param.ParameterType);
		}
		void addParameterDefinition(ParameterDefinition param) {
			if (param == null)
				return;
			addParameterReference(param);
			pushMember(param.Method as MemberReference);
			access(() => addCustomAttributes(param.CustomAttributes));
		}
		void addMethodReturnType(MethodReturnType methodReturnType) {
			if (methodReturnType == null)
				return;
			pushMember(methodReturnType.Method as MemberReference);
			pushMember(methodReturnType.ReturnType);
			addParameterDefinition(methodReturnType.Parameter);
		}
		void addGenericParameter(GenericParameter param) {
			if (param == null)
				return;
			addTypeReference(param);
			pushMember(param.Owner as MemberReference);
			access(() => addCustomAttributes(param.CustomAttributes));
			addTypeReferences(param.Constraints);
		}
		void addTypeReference(TypeReference typeReference) {
			if (typeReference == null)
				return;
			addMemberReference(typeReference);
			addGenericParameters(typeReference.GenericParameters);
		}
		void addMethodDefinition(MethodDefinition methodDefinition) {
			if (methodDefinition == null)
				return;
			addMethodReference(methodDefinition);
			access(() => addCustomAttributes(methodDefinition.CustomAttributes));
			addSecurityDeclarations(methodDefinition.SecurityDeclarations);
			addMethodReferences(methodDefinition.Overrides);
			addMethodBody(methodDefinition.Body);
		}
		void addSecurityDeclaration(SecurityDeclaration decl) {
			if (decl == null)
				return;
			access(() => addSecurityAttributes(decl.SecurityAttributes));
		}
		void addSecurityAttribute(SecurityAttribute attr) {
			if (attr == null)
				return;
			pushMember(attr.AttributeType);
			addCustomAttributeNamedArguments(attr.Fields);
			addCustomAttributeNamedArguments(attr.Properties);
		}
		void addMethodBody(MethodBody body) {
			if (body == null)
				return;
			pushMember(body.Method);
			addParameterDefinition(body.ThisParameter);
			addExceptionHandlers(body.ExceptionHandlers);
			addVariableDefinitions(body.Variables);
			addScope(body.Scope);
			addInstructions(body.Instructions);
		}
		void addExceptionHandler(ExceptionHandler handler) {
			if (handler == null)
				return;
			pushMember(handler.CatchType);
		}
		void addVariableDefinition(VariableDefinition v) {
			if (v == null)
				return;
			addVariableReference(v);
		}
		void addVariableReference(VariableReference v) {
			if (v == null)
				return;
			pushMember(v.VariableType);
		}
		void addScope(Scope scope) {
			if (scope == null)
				return;
			addVariableDefinitions(scope.Variables);
			addScopes(scope.Scopes);
		}
		void addGenericInstanceMethod(GenericInstanceMethod genericInstanceMethod) {
			if (genericInstanceMethod == null)
				return;
			addMethodSpecification(genericInstanceMethod);
			addTypeReferences(genericInstanceMethod.GenericArguments);
		}
		void addMethodSpecification(MethodSpecification methodSpecification) {
			if (methodSpecification == null)
				return;
			addMethodReference(methodSpecification);
			pushMember(methodSpecification.ElementMethod);
		}
		void addPropertyReference(PropertyReference propertyReference) {
			if (propertyReference == null)
				return;
			addMemberReference(propertyReference);
			pushMember(propertyReference.PropertyType);
		}
		void addPropertyDefinition(PropertyDefinition propertyDefinition) {
			if (propertyDefinition == null)
				return;
			addPropertyReference(propertyDefinition);
			access(() => addCustomAttributes(propertyDefinition.CustomAttributes));
			pushMember(propertyDefinition.GetMethod);
			pushMember(propertyDefinition.SetMethod);
			addMethodDefinitions(propertyDefinition.OtherMethods);
		}
		void addTypeDefinition(TypeDefinition typeDefinition) {
			if (typeDefinition == null)
				return;
			addTypeReference(typeDefinition);
			pushMember(typeDefinition.BaseType);
			addTypeReferences(typeDefinition.Interfaces);
			addTypeDefinitions(typeDefinition.NestedTypes);
			addMethodDefinitions(typeDefinition.Methods);
			addFieldDefinitions(typeDefinition.Fields);
			addEventDefinitions(typeDefinition.Events);
			addPropertyDefinitions(typeDefinition.Properties);
			access(() => addCustomAttributes(typeDefinition.CustomAttributes));
			addSecurityDeclarations(typeDefinition.SecurityDeclarations);
		}
		void addTypeSpecification(TypeSpecification ts) {
			if (ts == null)
				return;
			addTypeReference(ts);
			pushMember(ts.ElementType);
		}
		void addArrayType(ArrayType at) {
			if (at == null)
				return;
			addTypeSpecification(at);
		}
		void addFunctionPointerType(FunctionPointerType fpt) {
			if (fpt == null)
				return;
			addTypeSpecification(fpt);

			// It's an anon MethodReference created by the class. Not useful to us.
			//pushMember(fpt.function);
		}
		void addGenericInstanceType(GenericInstanceType git) {
			if (git == null)
				return;
			addTypeSpecification(git);
			addTypeReferences(git.GenericArguments);
		}
		void addOptionalModifierType(OptionalModifierType omt) {
			if (omt == null)
				return;
			addTypeSpecification(omt);
			pushMember(omt.ModifierType);
		}
		void addRequiredModifierType(RequiredModifierType rmt) {
			if (rmt == null)
				return;
			addTypeSpecification(rmt);
			pushMember(rmt.ModifierType);
		}
		void addPinnedType(PinnedType pt) {
			if (pt == null)
				return;
			addTypeSpecification(pt);
		}
		void addPointerType(PointerType pt) {
			if (pt == null)
				return;
			addTypeSpecification(pt);
		}
		void addByReferenceType(ByReferenceType brt) {
			if (brt == null)
				return;
			addTypeSpecification(brt);
		}
		void addSentinelType(SentinelType st) {
			if (st == null)
				return;
			addTypeSpecification(st);
		}
		void addCallSite(CallSite cs) {
			if (cs == null)
				return;
			pushMember(cs.signature);
		}

		void doEventDefinition(EventDefinition eventDefinition) {
			if (eventDefinitions.ContainsKey(eventDefinition))
				return;
			eventDefinitions[eventDefinition] = true;
			addEventDefinition(eventDefinition);
		}
		void doFieldReference(FieldReference fieldReference) {
			if (fieldReferences.ContainsKey(fieldReference))
				return;
			fieldReferences[fieldReference] = true;
			addFieldReference(fieldReference);
		}
		void doFieldDefinition(FieldDefinition fieldDefinition) {
			if (fieldDefinitions.ContainsKey(fieldDefinition))
				return;
			fieldDefinitions[fieldDefinition] = true;
			addFieldDefinition(fieldDefinition);
		}
		void doMethodReference(MethodReference methodReference) {
			if (methodReferences.ContainsKey(methodReference))
				return;
			methodReferences[methodReference] = true;
			addMethodReference(methodReference);
		}
		void doMethodDefinition(MethodDefinition methodDefinition) {
			if (methodDefinitions.ContainsKey(methodDefinition))
				return;
			methodDefinitions[methodDefinition] = true;
			addMethodDefinition(methodDefinition);
		}
		void doGenericInstanceMethod(GenericInstanceMethod genericInstanceMethod) {
			if (genericInstanceMethods.ContainsKey(genericInstanceMethod))
				return;
			genericInstanceMethods[genericInstanceMethod] = true;
			addGenericInstanceMethod(genericInstanceMethod);
		}
		void doPropertyDefinition(PropertyDefinition propertyDefinition) {
			if (propertyDefinitions.ContainsKey(propertyDefinition))
				return;
			propertyDefinitions[propertyDefinition] = true;
			addPropertyDefinition(propertyDefinition);
		}
		void doTypeReference(TypeReference typeReference) {
			if (typeReferences.ContainsKey(typeReference))
				return;
			typeReferences[typeReference] = true;
			addTypeReference(typeReference);
		}
		void doTypeDefinition(TypeDefinition typeDefinition) {
			if (typeDefinitions.ContainsKey(typeDefinition))
				return;
			typeDefinitions[typeDefinition] = true;
			addTypeDefinition(typeDefinition);
		}
		void doGenericParameter(GenericParameter genericParameter) {
			if (genericParameters.ContainsKey(genericParameter))
				return;
			genericParameters[genericParameter] = true;
			addGenericParameter(genericParameter);
		}
		void doArrayType(ArrayType arrayType) {
			if (arrayTypes.ContainsKey(arrayType))
				return;
			arrayTypes[arrayType] = true;
			addArrayType(arrayType);
		}
		void doFunctionPointerType(FunctionPointerType functionPointerType) {
			if (functionPointerTypes.ContainsKey(functionPointerType))
				return;
			functionPointerTypes[functionPointerType] = true;
			addFunctionPointerType(functionPointerType);
		}
		void doGenericInstanceType(GenericInstanceType genericInstanceType) {
			if (genericInstanceTypes.ContainsKey(genericInstanceType))
				return;
			genericInstanceTypes[genericInstanceType] = true;
			addGenericInstanceType(genericInstanceType);
		}
		void doOptionalModifierType(OptionalModifierType optionalModifierType) {
			if (optionalModifierTypes.ContainsKey(optionalModifierType))
				return;
			optionalModifierTypes[optionalModifierType] = true;
			addOptionalModifierType(optionalModifierType);
		}
		void doRequiredModifierType(RequiredModifierType requiredModifierType) {
			if (requiredModifierTypes.ContainsKey(requiredModifierType))
				return;
			requiredModifierTypes[requiredModifierType] = true;
			addRequiredModifierType(requiredModifierType);
		}
		void doPinnedType(PinnedType pinnedType) {
			if (pinnedTypes.ContainsKey(pinnedType))
				return;
			pinnedTypes[pinnedType] = true;
			addPinnedType(pinnedType);
		}
		void doPointerType(PointerType pointerType) {
			if (pointerTypes.ContainsKey(pointerType))
				return;
			pointerTypes[pointerType] = true;
			addPointerType(pointerType);
		}
		void doByReferenceType(ByReferenceType byReferenceType) {
			if (byReferenceTypes.ContainsKey(byReferenceType))
				return;
			byReferenceTypes[byReferenceType] = true;
			addByReferenceType(byReferenceType);
		}
		void doSentinelType(SentinelType sentinelType) {
			if (sentinelTypes.ContainsKey(sentinelType))
				return;
			sentinelTypes[sentinelType] = true;
			addSentinelType(sentinelType);
		}
	}
}
