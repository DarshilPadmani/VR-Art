using Oculus.Interaction;
using UnityEngine;

[DisallowMultipleComponent]
public class GrabHistoryRecorder : MonoBehaviour
{
    [SerializeField] private DrawingHistoryManager historyManager;
    [SerializeField] private GrabInteractable grabInteractable;

    private Vector3 _startPos;
    private Quaternion _startRot;
    private Vector3 _startScale;
    private bool _isSubscribed;

    private void Awake()
    {
        if (historyManager == null)
            historyManager = FindObjectOfType<DrawingHistoryManager>();

        if (grabInteractable == null)
            grabInteractable = GetComponent<GrabInteractable>();
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (_isSubscribed || grabInteractable == null)
            return;

        grabInteractable.WhenStateChanged += HandleStateChanged;
        _isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_isSubscribed || grabInteractable == null)
            return;

        grabInteractable.WhenStateChanged -= HandleStateChanged;
        _isSubscribed = false;
    }

    private void HandleStateChanged(InteractableStateChangeArgs args)
    {
        if (args.NewState == InteractableState.Select)
        {
            _startPos = transform.position;
            _startRot = transform.rotation;
            _startScale = transform.localScale;
            return;
        }

        if (args.PreviousState != InteractableState.Select)
            return;

        if (historyManager == null)
            return;

        Vector3 endPos = transform.position;
        Quaternion endRot = transform.rotation;
        Vector3 endScale = transform.localScale;

        if (!HasTransformChanged(_startPos, endPos, _startRot, endRot, _startScale, endScale))
            return;

        historyManager.RecordAction(new TransformAction(
            transform,
            _startPos,
            endPos,
            _startRot,
            endRot,
            _startScale,
            endScale));
    }

    private static bool HasTransformChanged(
        Vector3 startPos,
        Vector3 endPos,
        Quaternion startRot,
        Quaternion endRot,
        Vector3 startScale,
        Vector3 endScale)
    {
        return (startPos - endPos).sqrMagnitude > 0.000001f
            || Quaternion.Angle(startRot, endRot) > 0.01f
            || (startScale - endScale).sqrMagnitude > 0.000001f;
    }
}
