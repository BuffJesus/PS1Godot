-- Trigger-box test script. Attach to a PS1TriggerBox's ScriptFile.
-- Runtime calls onTriggerEnter(index) / onTriggerExit(index) as top-level
-- functions in this script (no self — trigger boxes aren't GameObjects).

function onTriggerEnter(index)
    Debug.Log("test_trigger: ENTER idx=" .. index)
end

function onTriggerExit(index)
    Debug.Log("test_trigger: EXIT idx=" .. index)
end
