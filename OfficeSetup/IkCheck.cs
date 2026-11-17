using UnityEngine;
// play mode: does the hand IK with zero lift leave the arm exactly where the animation has it? (compares IK on / off)
public class IkCheck
{
    public static string Execute()
    {
        string r = "";
        foreach (var l in Object.FindObjectsByType<OfficeListener>(FindObjectsSortMode.None))
        {
            if (!l.gameObject.activeInHierarchy) continue;
            var an = l.GetComponent<Animator>();
            var keepL = l.liftL; var keepR = l.liftR;
            l.liftL = new[] { Vector3.zero }; l.liftR = new[] { Vector3.zero };
            float t = an.GetCurrentAnimatorStateInfo(0).normalizedTime;
            OfficeListener.HandLiftOn = false; an.Play(OfficeListener.StillState, 0, t); an.Update(0f);
            Vector3 h0 = an.GetBoneTransform(HumanBodyBones.LeftHand).position, e0 = an.GetBoneTransform(HumanBodyBones.LeftLowerArm).position;
            Vector3 h1 = an.GetBoneTransform(HumanBodyBones.RightHand).position, e1 = an.GetBoneTransform(HumanBodyBones.RightLowerArm).position;
            OfficeListener.HandLiftOn = true; an.Play(OfficeListener.StillState, 0, t); an.Update(0f);
            float dh = Mathf.Max((an.GetBoneTransform(HumanBodyBones.LeftHand).position - h0).magnitude, (an.GetBoneTransform(HumanBodyBones.RightHand).position - h1).magnitude);
            float de = Mathf.Max((an.GetBoneTransform(HumanBodyBones.LeftLowerArm).position - e0).magnitude, (an.GetBoneTransform(HumanBodyBones.RightLowerArm).position - e1).magnitude);
            r += l.seat + " hand moved " + (dh * 100f).ToString("0.00") + " cm, elbow " + (de * 100f).ToString("0.00") + " cm\n";
            l.liftL = keepL; l.liftR = keepR;
        }
        return r;
    }
}
