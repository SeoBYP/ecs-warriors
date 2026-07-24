namespace Simulation.Data
{
    /// <summary>
    /// 몬스터 티어. 이게 곧 "어느 월드에서 사는가"를 가른다.
    /// - Normal : ECS 인스턴싱 군중 (수천~1만, 애니는 VAT 후속)
    /// - Elite  : GameObject (소수, Animator Override)
    /// - Boss   : GameObject (1~, Animator Override)
    /// Animator Override는 GameObject Animator 개념이라 1만 ECS엔 못 쓴다 → 티어=월드.
    /// </summary>
    public enum MonsterTier
    {
        Normal,
        Elite,
        Boss,
    }
}
