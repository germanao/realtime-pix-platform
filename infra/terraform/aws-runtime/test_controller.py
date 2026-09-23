import unittest
from controller import decide


class ControllerTests(unittest.TestCase):
    def test_first_visitor_starts_stopped_host(self):
        state, action, status = decide({}, 1000, "stopped", 500, True)
        self.assertEqual((action, status, state["used"]), ("start", "starting", 0))

    def test_repeated_wakes_never_start_a_second_instance(self):
        state, _, _ = decide({}, 1000, "stopped", 500, True)
        state, action, status = decide(state, 1030, "pending", 1000, True)
        self.assertIsNone(action)
        self.assertEqual(state["used"], 30)

    def test_idle_shutdown_and_active_visitor_renewal(self):
        state, _, _ = decide({}, 1000, "stopped", 500, True)
        _, action, _ = decide(state, 2201, "running", 1000, False)
        self.assertEqual(action, "stop")
        _, action, _ = decide(state, 2201, "running", 1000, True)
        self.assertIsNone(action)

    def test_budget_cannot_be_bypassed_by_wake_or_restart(self):
        state = {"day": 0, "used": 86390, "metered": 1000, "active": True, "activity": 1000}
        state, action, status = decide(state, 1030, "running", 1000, True)
        self.assertEqual((action, status), ("stop", "daily-limit"))
        _, action, status = decide(state, 1060, "stopped", 1000, True)
        self.assertEqual((action, status), (None, "daily-limit"))

    def test_midnight_only_charges_new_day_seconds(self):
        state = {"day": 0, "used": 86000, "metered": 86390, "active": True, "activity": 86390}
        state, action, _ = decide(state, 86410, "running", 86300, True)
        self.assertEqual(state["used"], 10)
        self.assertIsNone(action)

    def test_stopped_time_is_not_charged(self):
        state = {"day": 0, "used": 100, "metered": 1000, "active": False}
        state, _, _ = decide(state, 5000, "stopped", 900, True)
        self.assertEqual(state["used"], 100)


if __name__ == "__main__":
    unittest.main()
