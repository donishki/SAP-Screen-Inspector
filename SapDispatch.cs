using System.Collections;

using System.Dynamic;

using System.Globalization;

using System.Reflection;

using System.Runtime.ExceptionServices;

using System.Runtime.InteropServices;



namespace Inspector;



// Invoke COM members by name through IDispatch, without C# dynamic's ITypeInfo binding.

// Wrap every returned COM object so later session/control calls use the same path.

internal sealed class SapDispatch : DynamicObject, IEnumerable

{

    private readonly object target;

    private SapDispatch(object target) => this.target = target;

    internal static object? Wrap(object? value) => value is not null && Marshal.IsComObject(value) ? new SapDispatch(value) : value;

    internal static object? Unwrap(object? value) => value is SapDispatch dispatch ? dispatch.target : value;

    private object? Invoke(string name, BindingFlags flags, object?[]? arguments = null)

    {

        try

        {

            return Wrap(target.GetType().InvokeMember(name, flags, null, target,

                arguments?.Select(Unwrap).ToArray(), CultureInfo.InvariantCulture));

        }

        catch (TargetInvocationException ex) when (ex.InnerException is not null)

        {

            // Keep COM error codes intact for the existing SAP recovery handlers.

            ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw;

        }

    }

    public override bool TryGetMember(GetMemberBinder binder, out object? result)

    { result = Invoke(binder.Name, BindingFlags.GetProperty); return true; }

    public override bool TrySetMember(SetMemberBinder binder, object? value)

    { Invoke(binder.Name, BindingFlags.SetProperty, [value]); return true; }

    public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result)

    { result = Invoke(binder.Name, BindingFlags.InvokeMethod | BindingFlags.GetProperty, args); return true; }

    public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object? result)

    { result = Invoke("[DISPID=0]", BindingFlags.GetProperty | BindingFlags.InvokeMethod, indexes); return true; }

    public override bool TrySetIndex(SetIndexBinder binder, object[] indexes, object? value)

    { Invoke("[DISPID=0]", BindingFlags.SetProperty, indexes.Concat(new[] { value }).ToArray()); return true; }

    public IEnumerator GetEnumerator()

    {

        // SAP GuiCollection (including ColumnOrder) exposes Count and a default Item member.

        var count = Convert.ToInt32(Invoke("Count", BindingFlags.GetProperty), CultureInfo.InvariantCulture);

        for (var i = 0; i < count; i++) yield return Invoke("[DISPID=0]", BindingFlags.GetProperty | BindingFlags.InvokeMethod, [i]);

    }

}

