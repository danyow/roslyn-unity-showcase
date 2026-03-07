public sealed partial class GlobalEventConfig
{
    public void Trigger(Luban.BeanBase caller, float delay, bool force)
    {
        UnityEngine.Debug.Log($"[GlobalEventConfig] Trigger 被调用, caller: {caller}, delay: {delay}, force: {force}");
    }
}
