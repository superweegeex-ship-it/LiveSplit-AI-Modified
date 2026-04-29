using System;
using System.Runtime.InteropServices;
using System.Text;

using TimeSpan = System.TimeSpan;

namespace LiveSplit.AutoSplittingRuntime;

public class RuntimeRef
{
    internal IntPtr ptr;
    internal RuntimeRef(IntPtr ptr)
    {
        this.ptr = ptr;
    }
}

public class RuntimeRefMut : RuntimeRef
{
    internal RuntimeRefMut(IntPtr ptr) : base(ptr) { }
}

public class Runtime : RuntimeRefMut, IDisposable
{
    private void Drop()
    {
        if (ptr != IntPtr.Zero)
        {
            ASRNative.Runtime_drop(ptr);
            ptr = IntPtr.Zero;
        }
    }
    ~Runtime()
    {
        Drop();
    }
    public void Dispose()
    {
        Drop();
        GC.SuppressFinalize(this);
    }
    public Runtime(
        string path,
        SettingsMap settingsMap,
        StateDelegate state,
        IndexDelegate index,
        SegmentSplittedDelegate segmentSplitted,
        Action start,
        Action split,
        Action skipSplit,
        Action undoSplit,
        Action reset,
        SetGameTimeDelegate setGameTime,
        Action pauseGameTime,
        Action resumeGameTime,
        SetCustomVariableDelegate setCustomVariable,
        LogDelegate log
    ) : base(IntPtr.Zero)
    {
        IntPtr settingsMapPtr = settingsMap?.ptr ?? IntPtr.Zero;
        if (settingsMap != null)
        {
            settingsMap.ptr = IntPtr.Zero;
        }

        ptr = ASRNative.Runtime_new(
            path,
            settingsMapPtr,
            state,
            index,
            segmentSplitted,
            start,
            split,
            skipSplit,
            undoSplit,
            reset,
            setGameTime,
            pauseGameTime,
            resumeGameTime,
            setCustomVariable,
            log
        );
        if (ptr == IntPtr.Zero)
        {
            throw new ArgumentException("Couldn't load the module provided.");
        }
    }
    internal Runtime(IntPtr ptr) : base(ptr) { }

    public bool Step()
    {
        if (ptr == IntPtr.Zero)
        {
            return false;
        }

        return ASRNative.Runtime_step(ptr);
    }

    public TimeSpan TickRate()
    {
        if (ptr == IntPtr.Zero)
        {
            return TimeSpan.Zero;
        }

        return new TimeSpan((long)ASRNative.Runtime_tick_rate(ptr));
    }

    public Widgets GetSettingsWidgets()
    {
        if (ptr == IntPtr.Zero)
        {
            return null;
        }

        return new Widgets(ASRNative.Runtime_get_settings_widgets(ptr));
    }

    public void SettingsMapSetBool(string key, bool value)
    {
        if (ptr == IntPtr.Zero)
        {
            return;
        }

        ASRNative.Runtime_settings_map_set_bool(ptr, key, value ? (byte)1 : (byte)0);
    }

    public void SettingsMapSetString(string key, string value)
    {
        if (ptr == IntPtr.Zero)
        {
            return;
        }

        ASRNative.Runtime_settings_map_set_string(ptr, key, value);
    }

    public SettingsMap GetSettingsMap()
    {
        if (ptr == IntPtr.Zero)
        {
            return null;
        }

        return new SettingsMap(ASRNative.Runtime_get_settings_map(ptr));
    }

    public void SetSettingsMap(SettingsMap settingsMap)
    {
        if (ptr == IntPtr.Zero)
        {
            return;
        }

        IntPtr settingsMapPtr = settingsMap.ptr;
        if (settingsMapPtr == IntPtr.Zero)
        {
            return;
        }

        settingsMap.ptr = IntPtr.Zero;
        ASRNative.Runtime_set_settings_map(ptr, settingsMapPtr);
    }

    public bool AreSettingsChanged(SettingsMapRef previousSettingsMap, WidgetsRef previousWidgets)
    {
        if (ptr == IntPtr.Zero)
        {
            return false;
        }

        if (previousSettingsMap.ptr == IntPtr.Zero)
        {
            return false;
        }

        if (previousWidgets.ptr == IntPtr.Zero)
        {
            return false;
        }

        return ASRNative.Runtime_are_settings_changed(ptr, previousSettingsMap.ptr, previousWidgets.ptr) != 0;
    }
}

public class SettingsMapRef
{
    internal IntPtr ptr;
    internal SettingsMapRef(IntPtr ptr)
    {
        this.ptr = ptr;
    }
    public ulong GetLength()
    {
        if (ptr == IntPtr.Zero)
        {
            return 0;
        }

        return (ulong)ASRNative.SettingsMap_len(ptr);
    }
    public string GetKey(ulong index)
    {
        if (ptr == IntPtr.Zero)
        {
            return "";
        }

        return ASRNative.SettingsMap_get_key(ptr, (UIntPtr)index);
    }
    public SettingValueRef GetValue(ulong index)
    {
        if (ptr == IntPtr.Zero)
        {
            return null;
        }

        return new SettingValueRef(ASRNative.SettingsMap_get_value(ptr, (UIntPtr)index));
    }
    public SettingValueRef KeyGetValue(string key)
    {
        if (ptr == IntPtr.Zero)
        {
            return null;
        }

        IntPtr valuePtr = ASRNative.SettingsMap_get_value_by_key(ptr, key);
        if (valuePtr != IntPtr.Zero)
        {
            return new SettingValueRef(valuePtr);
        }
        else
        {
            return null;
        }
    }
}

public class SettingsMapRefMut : SettingsMapRef
{
    internal SettingsMapRefMut(IntPtr ptr) : base(ptr) { }
    public void Insert(string key, SettingValue value)
    {
        if (ptr == IntPtr.Zero)
        {
            return;
        }

        IntPtr valuePtr = value.ptr;
        if (valuePtr == IntPtr.Zero)
        {
            return;
        }

        value.ptr = IntPtr.Zero;
        ASRNative.SettingsMap_insert(ptr, key, valuePtr);
    }
}

public class SettingsMap : SettingsMapRefMut, IDisposable
{
    private void Drop()
    {
        if (ptr != IntPtr.Zero)
        {
            ASRNative.SettingsMap_drop(ptr);
            ptr = IntPtr.Zero;
        }
    }
    ~SettingsMap()
    {
        Drop();
    }
    public void Dispose()
    {
        Drop();
        GC.SuppressFinalize(this);
    }
    public SettingsMap() : base(IntPtr.Zero)
    {
        ptr = ASRNative.SettingsMap_new();
        if (ptr == IntPtr.Zero)
        {
            throw new ArgumentException("Couldn't create the settings map.");
        }
    }
    internal SettingsMap(IntPtr ptr) : base(ptr) { }
}

public class SettingsListRef
{
    internal IntPtr ptr;
    internal SettingsListRef(IntPtr ptr)
    {
        this.ptr = ptr;
    }
    public ulong GetLength()
    {
        if (ptr == IntPtr.Zero)
        {
            return 0;
        }

        return (ulong)ASRNative.SettingsList_len(ptr);
    }
    public SettingValueRef Get(ulong index)
    {
        if (ptr == IntPtr.Zero)
        {
            return null;
        }

        return new SettingValueRef(ASRNative.SettingsList_get(ptr, (UIntPtr)index));
    }
}

public class SettingsListRefMut : SettingsListRef
{
    internal SettingsListRefMut(IntPtr ptr) : base(ptr) { }
    public void Push(SettingValue value)
    {
        if (ptr == IntPtr.Zero)
        {
            return;
        }

        IntPtr valuePtr = value.ptr;
        if (valuePtr == IntPtr.Zero)
        {
            return;
        }

        value.ptr = IntPtr.Zero;
        ASRNative.SettingsList_push(ptr, valuePtr);
    }
}

public class SettingsList : SettingsListRefMut, IDisposable
{
    private void Drop()
    {
        if (ptr != IntPtr.Zero)
        {
            ASRNative.SettingsList_drop(ptr);
            ptr = IntPtr.Zero;
        }
    }
    ~SettingsList()
    {
        Drop();
    }
    public void Dispose()
    {
        Drop();
        GC.SuppressFinalize(this);
    }
    public SettingsList() : base(IntPtr.Zero)
    {
        ptr = ASRNative.SettingsList_new();
        if (ptr == IntPtr.Zero)
        {
            throw new ArgumentException("Couldn't create the settings list.");
        }
    }
    internal SettingsList(IntPtr ptr) : base(ptr) { }
}

public class SettingValueRef
{
    internal IntPtr ptr;
    internal SettingValueRef(IntPtr ptr)
    {
        this.ptr = ptr;
    }
    public string GetKind()
    {
        if (ptr == IntPtr.Zero)
        {
            return "";
        }

        UIntPtr ty = ASRNative.SettingValue_get_type(ptr);
        return (ulong)ty switch
        {
            1 => "map",
            2 => "list",
            3 => "bool",
            4 => "i64",
            5 => "f64",
            6 => "string",
            _ => "",
        };
    }
    public SettingsMapRef GetMap()
    {
        if (ptr == IntPtr.Zero)
        {
            return null;
        }

        return new SettingsMapRef(ASRNative.SettingValue_get_map(ptr));
    }
    public SettingsListRef GetList()
    {
        if (ptr == IntPtr.Zero)
        {
            return null;
        }

        return new SettingsListRef(ASRNative.SettingValue_get_list(ptr));
    }
    public bool GetBool()
    {
        if (ptr == IntPtr.Zero)
        {
            return false;
        }

        return ASRNative.SettingValue_get_bool(ptr) != 0;
    }
    public long GetI64()
    {
        if (ptr == IntPtr.Zero)
        {
            return 0;
        }

        return ASRNative.SettingValue_get_i64(ptr);
    }
    public double GetF64()
    {
        if (ptr == IntPtr.Zero)
        {
            return 0;
        }

        return ASRNative.SettingValue_get_f64(ptr);
    }
    public string GetString()
    {
        if (ptr == IntPtr.Zero)
        {
            return "";
        }

        return ASRNative.SettingValue_get_string(ptr);
    }
}

public class SettingValueRefMut : SettingValueRef
{
    internal SettingValueRefMut(IntPtr ptr) : base(ptr) { }
}

public class SettingValue : SettingValueRefMut, IDisposable
{
    private void Drop()
    {
        if (ptr != IntPtr.Zero)
        {
            ASRNative.SettingValue_drop(ptr);
            ptr = IntPtr.Zero;
        }
    }
    ~SettingValue()
    {
        Drop();
    }
    public void Dispose()
    {
        Drop();
        GC.SuppressFinalize(this);
    }
    public SettingValue(bool value) : base(IntPtr.Zero)
    {
        ptr = ASRNative.SettingValue_new_bool(value ? (byte)1 : (byte)0);
        if (ptr == IntPtr.Zero)
        {
            throw new ArgumentException("Couldn't create the setting value.");
        }
    }
    public SettingValue(long value) : base(IntPtr.Zero)
    {
        ptr = ASRNative.SettingValue_new_i64(value);
        if (ptr == IntPtr.Zero)
        {
            throw new ArgumentException("Couldn't create the setting value.");
        }
    }
    public SettingValue(double value) : base(IntPtr.Zero)
    {
        ptr = ASRNative.SettingValue_new_f64(value);
        if (ptr == IntPtr.Zero)
        {
            throw new ArgumentException("Couldn't create the setting value.");
        }
    }
    public SettingValue(string value) : base(IntPtr.Zero)
    {
        ptr = ASRNative.SettingValue_new_string(value);
        if (ptr == IntPtr.Zero)
        {
            throw new ArgumentException("Couldn't create the setting value.");
        }
    }
    public SettingValue(SettingsMap value) : base(IntPtr.Zero)
    {
        IntPtr valuePtr = value.ptr;
        if (valuePtr == IntPtr.Zero)
        {
            throw new ArgumentException("Couldn't create the setting value.");
        }

        value.ptr = IntPtr.Zero;
        ptr = ASRNative.SettingValue_new_map(valuePtr);
        if (ptr == IntPtr.Zero)
        {
            throw new ArgumentException("Couldn't create the setting value.");
        }
    }
    public SettingValue(SettingsList value) : base(IntPtr.Zero)
    {
        IntPtr valuePtr = value.ptr;
        if (valuePtr == IntPtr.Zero)
        {
            throw new ArgumentException("Couldn't create the setting value.");
        }

        value.ptr = IntPtr.Zero;
        ptr = ASRNative.SettingValue_new_list(valuePtr);
        if (ptr == IntPtr.Zero)
        {
            throw new ArgumentException("Couldn't create the setting value.");
        }
    }
    internal SettingValue(IntPtr ptr) : base(ptr) { }
}

public class WidgetsRef
{
    internal IntPtr ptr;
    internal WidgetsRef(IntPtr ptr)
    {
        this.ptr = ptr;
    }

    public ulong GetLength()
    {
        if (ptr == IntPtr.Zero)
        {
            return 0;
        }

        return (ulong)ASRNative.Widgets_len(ptr);
    }

    public string GetKey(ulong index)
    {
        if (ptr == IntPtr.Zero)
        {
            return "";
        }

        return ASRNative.Widgets_get_key(ptr, (UIntPtr)index);
    }

    public string GetDescription(ulong index)
    {
        if (ptr == IntPtr.Zero)
        {
            return "";
        }

        return ASRNative.Widgets_get_description(ptr, (UIntPtr)index);
    }

    public string GetTooltip(ulong index)
    {
        if (ptr == IntPtr.Zero)
        {
            return "";
        }

        return ASRNative.Widgets_get_tooltip(ptr, (UIntPtr)index);
    }

    public uint GetHeadingLevel(ulong index)
    {
        if (ptr == IntPtr.Zero)
        {
            return 0;
        }

        return ASRNative.Widgets_get_heading_level(ptr, (UIntPtr)index);
    }

    public string GetType(ulong index)
    {
        if (ptr == IntPtr.Zero)
        {
            return "";
        }

        UIntPtr ty = ASRNative.Widgets_get_type(ptr, (UIntPtr)index);
        return (ulong)ty switch
        {
            1 => "bool",
            2 => "title",
            3 => "choice",
            4 => "file-select",
            _ => "",
        };
    }

    public bool GetBool(ulong index, SettingsMapRef settingsMap)
    {
        if (ptr == IntPtr.Zero)
        {
            return false;
        }

        if (settingsMap.ptr == IntPtr.Zero)
        {
            return false;
        }

        return ASRNative.Widgets_get_bool(ptr, (UIntPtr)index, settingsMap.ptr) != 0;
    }

    public ulong GetChoiceCurrentIndex(ulong index, SettingsMapRef settingsMap)
    {
        if (ptr == IntPtr.Zero)
        {
            return 0;
        }

        if (settingsMap.ptr == IntPtr.Zero)
        {
            return 0;
        }

        return (ulong)ASRNative.Widgets_get_choice_current_index(ptr, (UIntPtr)index, settingsMap.ptr);
    }

    public ulong GetChoiceOptionsLength(ulong index)
    {
        if (ptr == IntPtr.Zero)
        {
            return 0;
        }

        return (ulong)ASRNative.Widgets_get_choice_options_len(ptr, (UIntPtr)index);
    }

    public string GetChoiceOptionKey(ulong index, ulong optionIndex)
    {
        if (ptr == IntPtr.Zero)
        {
            return "";
        }

        return ASRNative.Widgets_get_choice_option_key(ptr, (UIntPtr)index, (UIntPtr)optionIndex);
    }

    public string GetChoiceOptionDescription(ulong index, ulong optionIndex)
    {
        if (ptr == IntPtr.Zero)
        {
            return "";
        }

        return ASRNative.Widgets_get_choice_option_description(ptr, (UIntPtr)index, (UIntPtr)optionIndex);
    }

    public string GetFileSelectFilter(ulong index)
    {
        if (ptr == IntPtr.Zero)
        {
            return "";
        }

        return ASRNative.Widgets_get_file_select_filter(ptr, (UIntPtr)index);
    }
}

public class WidgetsRefMut : WidgetsRef
{
    internal WidgetsRefMut(IntPtr ptr) : base(ptr) { }
}

public class Widgets : WidgetsRefMut, IDisposable
{
    private void Drop()
    {
        if (ptr != IntPtr.Zero)
        {
            ASRNative.Widgets_drop(ptr);
            ptr = IntPtr.Zero;
        }
    }
    ~Widgets()
    {
        Drop();
    }
    public void Dispose()
    {
        Drop();
        GC.SuppressFinalize(this);
    }
    internal Widgets(IntPtr ptr) : base(ptr) { }
}

public delegate int StateDelegate();
public delegate int IndexDelegate();
public delegate int SegmentSplittedDelegate(int idx);
public delegate void SetGameTimeDelegate(long gameTime);
public delegate void SetCustomVariableDelegate(IntPtr namePtr, UIntPtr nameLen, IntPtr valuePtr, UIntPtr valueLen);
public delegate void LogDelegate(IntPtr messagePtr, UIntPtr messageLen);

public static class ASRNative
{
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr Runtime_new(
        ASRString path,
        IntPtr settings_map,
        StateDelegate state,
        IndexDelegate index,
        SegmentSplittedDelegate segmentSplitted,
        Action start,
        Action split,
        Action skipSplit,
        Action undoSplit,
        Action reset,
        SetGameTimeDelegate set_game_time,
        Action pause_game_time,
        Action resume_game_time,
        SetCustomVariableDelegate set_custom_variable,
        LogDelegate log
    );
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Runtime_drop(IntPtr self);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern bool Runtime_step(IntPtr self);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern ulong Runtime_tick_rate(IntPtr self);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr Runtime_get_settings_widgets(IntPtr self);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Runtime_settings_map_set_bool(IntPtr self, ASRString key, byte value);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Runtime_settings_map_set_string(IntPtr self, ASRString key, ASRString value);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr Runtime_get_settings_map(IntPtr self);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Runtime_set_settings_map(IntPtr self, IntPtr settings_map);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern byte Runtime_are_settings_changed(IntPtr self, IntPtr previous_settings_map, IntPtr previous_widgets);

    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SettingsMap_new();
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SettingsMap_drop(IntPtr self);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SettingsMap_insert(IntPtr self, ASRString key, IntPtr value);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern UIntPtr SettingsMap_len(IntPtr self);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern ASRString SettingsMap_get_key(IntPtr self, UIntPtr index);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SettingsMap_get_value(IntPtr self, UIntPtr index);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SettingsMap_get_value_by_key(IntPtr self, ASRString key);

    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SettingsList_new();
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SettingsList_drop(IntPtr self);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SettingsList_push(IntPtr self, IntPtr value);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern UIntPtr SettingsList_len(IntPtr self);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SettingsList_get(IntPtr self, UIntPtr index);

    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SettingValue_new_map(IntPtr value);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SettingValue_new_list(IntPtr value);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SettingValue_new_bool(byte value);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SettingValue_new_i64(long value);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SettingValue_new_f64(double value);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SettingValue_new_string(ASRString value);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SettingValue_drop(IntPtr self);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern UIntPtr SettingValue_get_type(IntPtr self);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SettingValue_get_map(IntPtr self);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SettingValue_get_list(IntPtr self);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern byte SettingValue_get_bool(IntPtr self);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern long SettingValue_get_i64(IntPtr self);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern double SettingValue_get_f64(IntPtr self);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern ASRString SettingValue_get_string(IntPtr self);

    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Widgets_drop(IntPtr self);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern UIntPtr Widgets_len(IntPtr self);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern ASRString Widgets_get_key(IntPtr self, UIntPtr index);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern ASRString Widgets_get_description(IntPtr self, UIntPtr index);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern ASRString Widgets_get_tooltip(IntPtr self, UIntPtr index);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Widgets_get_heading_level(IntPtr self, UIntPtr index);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern UIntPtr Widgets_get_type(IntPtr self, UIntPtr index);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern byte Widgets_get_bool(IntPtr self, UIntPtr index, IntPtr settings_map);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern UIntPtr Widgets_get_choice_current_index(IntPtr self, UIntPtr index, IntPtr settings_map);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern UIntPtr Widgets_get_choice_options_len(IntPtr self, UIntPtr index);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern ASRString Widgets_get_choice_option_key(IntPtr self, UIntPtr index, UIntPtr option_index);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern ASRString Widgets_get_choice_option_description(IntPtr self, UIntPtr index, UIntPtr option_index);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern ASRString Widgets_get_file_select_filter(IntPtr self, UIntPtr index);

    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern UIntPtr get_buf_len();
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern ASRString path_to_wasi(ASRString original_path);
    [DllImport("asr_capi", CallingConvention = CallingConvention.Cdecl)]
    public static extern ASRString wasi_to_path(ASRString wasi_path);
}

public class ASRString : SafeHandle
{
    private bool needToFree;

    public ASRString() : base((IntPtr)0, false) { }

    public override bool IsInvalid => false;

    public static implicit operator ASRString(string managedString)
    {
        var asrString = new ASRString();

        int len = Encoding.UTF8.GetByteCount(managedString);
        byte[] buffer = new byte[len + 1];
        Encoding.UTF8.GetBytes(managedString, 0, managedString.Length, buffer, 0);
        IntPtr nativeUtf8 = Marshal.AllocHGlobal(buffer.Length);
        Marshal.Copy(buffer, 0, nativeUtf8, buffer.Length);

        asrString.SetHandle(nativeUtf8);
        asrString.needToFree = true;
        return asrString;
    }

    public static string FromPtrLen(IntPtr ptr, UIntPtr len)
    {
        if (ptr == IntPtr.Zero || (ulong)len > int.MaxValue)
        {
            return null;
        }

        unsafe
        {
            return Encoding.UTF8.GetString((byte*)ptr, (int)len);
        }
    }

    public static implicit operator string(ASRString asrString)
    {
        return FromPtrLen(asrString.handle, ASRNative.get_buf_len());
    }

    protected override bool ReleaseHandle()
    {
        if (needToFree)
        {
            Marshal.FreeHGlobal(handle);
        }

        return true;
    }
}
