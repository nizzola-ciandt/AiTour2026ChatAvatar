import { useState } from "react";
import RegisterForm, { UserData } from "./RegisterForm";
import AvatarChat from "./AvatarChat";

type AppScreen = "register" | "avatar";

function App() {
    const [currentScreen, setCurrentScreen] = useState<AppScreen>("register");
    const [userData, setUserData] = useState<UserData | null>(null);

    const handleRegisterComplete = (data: UserData) => {
        setUserData(data);
        setCurrentScreen("avatar");
    };

    const handleEndConversation = () => {
        setUserData(null);
        setCurrentScreen("register");
    };

    if (currentScreen === "register") {
        return <RegisterForm onRegisterComplete={handleRegisterComplete} />;
    }

    if (currentScreen === "avatar" && userData) {
        return <AvatarChat userData={userData} onEndConversation={handleEndConversation} />;
    }

    // Fallback - nunca deveria chegar aqui
    return <RegisterForm onRegisterComplete={handleRegisterComplete} />;
}

export default App;