import { useState, FormEvent } from "react";

interface RegisterFormProps {
    onRegisterComplete: (userData: UserData) => void;
}

export interface UserData {
    name: string;
    email: string;
    phone: string;
    position?: string;  // Cargo (opcional)
    company?: string;   // Empresa (opcional)
}

const BACKEND_HTTP_BASE = (import.meta.env.VITE_BACKEND_BASE as string | undefined) ?? window.location.origin;

function RegisterForm({ onRegisterComplete }: RegisterFormProps) {
    const [name, setName] = useState("");
    const [email, setEmail] = useState("");
    const [phone, setPhone] = useState("");
    const [position, setPosition] = useState("");
    const [company, setCompany] = useState("");
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const handleSubmit = async (e: FormEvent) => {
        e.preventDefault();
        setError(null);
        
        // Validação básica - apenas campos obrigatórios
        if (!name.trim() || !email.trim() || !phone.trim()) {
            setError("Nome, email e telefone são obrigatórios");
            return;
        }

        // Validação de email
        const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
        if (!emailRegex.test(email)) {
            setError("Por favor, insira um email válido");
            return;
        }

        // Validação de telefone (formato básico)
        const phoneRegex = /^[\d\s\-\(\)\+]+$/;
        if (!phoneRegex.test(phone)) {
            setError("Por favor, insira um telefone válido");
            return;
        }

        setLoading(true);
        console.log("entrou no try");

        try {
            const userData: UserData = {
                name: name.trim(),
                email: email.trim(),
                phone: phone.trim()
            };

            // Adicionar campos opcionais apenas se preenchidos
            if (position.trim()) {
                userData.position = position.trim();
            }
            if (company.trim()) {
                userData.company = company.trim();
            }
            console.log("vai postar em:" + BACKEND_HTTP_BASE);

            const response = await fetch(`${BACKEND_HTTP_BASE}/createuser`, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                },
                body: JSON.stringify(userData),
            });

            console.log("resposta" + response.status);

            if (!response.ok) {
                throw new Error(`Erro ao cadastrar: ${response.status}`);
            }

            // Sucesso - navega para a tela do avatar
            onRegisterComplete(userData);
        } catch (err) {
            setError(err instanceof Error ? err.message : "Erro ao realizar cadastro");
            setLoading(false);
        }
    };

    return (
        <div className="register-container">
            <div className="register-card">
                <div className="register-header">
                    <h1>CI&T - Azure Voice Live Avatar</h1>
                    <p>Cadastre-se para iniciar sua experiência com nosso assistente virtual</p>
                </div>

                <form onSubmit={handleSubmit} className="register-form">
                    <div className="form-group">
                        <label htmlFor="name">
                            Nome Completo <span className="required">*</span>
                        </label>
                        <input
                            id="name"
                            type="text"
                            value={name}
                            onChange={(e) => setName(e.target.value)}
                            placeholder="Digite seu nome completo"
                            disabled={loading}
                            required
                        />
                    </div>

                    <div className="form-group">
                        <label htmlFor="email">
                            E-mail <span className="required">*</span>
                        </label>
                        <input
                            id="email"
                            type="email"
                            value={email}
                            onChange={(e) => setEmail(e.target.value)}
                            placeholder="seu.email@exemplo.com"
                            disabled={loading}
                            required
                        />
                    </div>

                    <div className="form-group">
                        <label htmlFor="phone">
                            Telefone <span className="required">*</span>
                        </label>
                        <input
                            id="phone"
                            type="tel"
                            value={phone}
                            onChange={(e) => setPhone(e.target.value)}
                            placeholder="(11) 98765-4321"
                            disabled={loading}
                            required
                        />
                    </div>

                    <div className="optional-fields-divider">
                        <span>Informações opcionais</span>
                    </div>

                    <div className="form-group">
                        <label htmlFor="position">
                            Cargo <span className="optional-badge">Opcional</span>
                        </label>
                        <input
                            id="position"
                            type="text"
                            value={position}
                            onChange={(e) => setPosition(e.target.value)}
                            placeholder="Ex: Desenvolvedor, Gerente, Analista..."
                            disabled={loading}
                        />
                    </div>

                    <div className="form-group">
                        <label htmlFor="company">
                            Empresa <span className="optional-badge">Opcional</span>
                        </label>
                        <input
                            id="company"
                            type="text"
                            value={company}
                            onChange={(e) => setCompany(e.target.value)}
                            placeholder="Nome da empresa onde trabalha"
                            disabled={loading}
                        />
                    </div>

                    {error && (
                        <div className="error-message">
                            ⚠️ {error}
                        </div>
                    )}

                    <button
                        type="submit"
                        className="register-submit-button"
                        disabled={loading}
                    >
                        {loading ? (
                            <>
                                <span className="button-spinner"></span>
                                Cadastrando...
                            </>
                        ) : (
                            "Iniciar Conversa com o Avatar"
                        )}
                    </button>
                </form>

                <div className="register-footer">
                    <p>
                        <span className="required">*</span> Campos obrigatórios
                        <br />
                        Seus dados serão utilizados apenas para esta sessão
                    </p>
                </div>
            </div>
        </div>
    );
}

export default RegisterForm;